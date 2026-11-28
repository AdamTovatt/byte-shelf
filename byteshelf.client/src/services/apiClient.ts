import createClient from 'openapi-fetch';
import type { paths } from '../types/api';
import { getFreshToken, renewAccessToken } from './authService';
import { ERROR_CODES } from './errorCodes';

/** How long to wait before asking again when the server says to, but not for how many seconds. */
const ASK_AGAIN_SECONDS = 5;

/** A refusal from the server, with the machine-readable code from its error body when it gave one. */
export class ApiError extends Error {
  public status: number;
  public code: string | null;
  /** After how many seconds the server said to ask again; null when it did not. */
  public askAgainSeconds: number | null;

  constructor(message: string, status: number, code: string | null, askAgainSeconds: number | null = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.askAgainSeconds = askAgainSeconds;
  }
}

/** The fewest seconds to wait before asking again for what the server is still making, though it says fewer: none would ask on and on. */
const LEAST_NOT_READY_SECONDS = 1;

/**
 * After how many seconds to ask again for what the server is still making; null when the error is another. The
 * answer to a HEAD has no body to tell it by, so a 503 without a code is taken for one.
 */
export function notReadyFor(error: unknown): number | null {
  if (!(error instanceof ApiError) || error.status !== 503 || error.askAgainSeconds === null) return null;
  if ((error.code ?? ERROR_CODES.notReady) !== ERROR_CODES.notReady) return null;
  return Math.max(error.askAgainSeconds, LEAST_NOT_READY_SECONDS);
}

/** Whether the server refused because what was asked for is not there. */
export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

/** Whether the server refused because a shelf has no room for what was asked for. */
export function isNoRoom(error: unknown): boolean {
  return error instanceof ApiError && error.status === 507;
}

interface ApiFetchResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

interface ErrorBody {
  message?: string;
  code?: string;
}

/**
 * Sends a request with the session's access token. A 401 with a challenge (WWW-Authenticate) means
 * the token itself was refused, so it is renewed and the request sent once more; a 401 without one
 * is an answer about something else, such as a wrong current password, and is returned as it is.
 */
async function sendAuthorized(request: Request, send: (request: Request) => Promise<Response>): Promise<Response> {
  const token = await getFreshToken();
  if (token) {
    request.headers.set('Authorization', `Bearer ${token}`);
  }

  const retry = request.clone();
  const response = await send(request);
  if (response.status !== 401 || !response.headers.has('WWW-Authenticate')) {
    return response;
  }

  // None when the session has ended, or could not be renewed just now, such as while the server is
  // unreachable, and is kept for the next request.
  const renewed = await renewAccessToken(token);
  if (!renewed) return response;

  retry.headers.set('Authorization', `Bearer ${renewed}`);
  return send(retry);
}

export const apiClient = createClient<paths>({
  baseUrl: '',
  fetch: (request: Request) => sendAuthorized(request, (sent) => fetch(sent)),
});

/** The `{name}` parameters in a path template, such as 'shelf' | 'uploadId'. */
type PathParams<P extends string> = P extends `${string}{${infer Name}}${infer Rest}` ? Name | PathParams<Rest> : never;

/**
 * The URL of one of the server's paths, each `{name}` in `template` filled in escaped: for requests
 * made with authorizedFetch or authorizedUpload, so their paths are checked against the API description as apiClient's are.
 */
export function apiUrl<P extends keyof paths>(template: P, params: Record<PathParams<P>, string>, query?: URLSearchParams): string {
  const path = template.replace(/\{(\w+)\}/g, (_, name: PathParams<P>) => encodeURIComponent(params[name]));
  return query ? `${path}?${query}` : path;
}

/** For requests openapi-fetch does not suit, such as reading a file's bytes. */
export function authorizedFetch(input: string, init: RequestInit): Promise<Response> {
  return sendAuthorized(new Request(input, init), (request) => fetch(request));
}

/** A request that sends a file's bytes. */
export interface UploadInit {
  method: string;
  body: Blob;
  headers?: Record<string, string>;
  signal?: AbortSignal;
  /** Told how many of the bytes have gone out, as they go; sent again after a renewal, from the start again. */
  onSent?: (sentBytes: number) => void;
}

/** Sends a file's bytes as `authorizedFetch` sends a request, telling how many of them have gone out, which `fetch` cannot tell. */
export function authorizedUpload(url: string, { method, body, headers, signal, onSent }: UploadInit): Promise<Response> {
  // The request carries no body: XMLHttpRequest sends the blob itself, which a Request would only copy.
  return sendAuthorized(new Request(url, { method, headers }), (request) => sendWithProgress(request, body, signal, onSent));
}

/** Statuses whose answer has no body, which a Response refuses one for. */
const NULL_BODY_STATUSES = new Set([101, 204, 205, 304]);

/** Sends `request` with `body` through XMLHttpRequest, whose upload tells of each part sent, and answers as `fetch` would. */
function sendWithProgress(request: Request, body: Blob, signal: AbortSignal | undefined, onSent: ((sentBytes: number) => void) | undefined): Promise<Response> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason as Error);
      return;
    }

    const xhr = new XMLHttpRequest();
    const abort = () => xhr.abort();
    signal?.addEventListener('abort', abort, { once: true });
    const settle = (end: () => void) => () => {
      signal?.removeEventListener('abort', abort);
      end();
    };

    xhr.open(request.method, request.url);
    request.headers.forEach((value, name) => xhr.setRequestHeader(name, value));
    xhr.responseType = 'blob';
    if (onSent) xhr.upload.onprogress = (event) => onSent(event.loaded);
    xhr.onload = settle(() => resolve(new Response(NULL_BODY_STATUSES.has(xhr.status) ? null : (xhr.response as Blob), {
      status: xhr.status,
      statusText: xhr.statusText,
      headers: responseHeaders(xhr.getAllResponseHeaders()),
    })));
    // As fetch fails: a TypeError for want of an answer, the signal's reason once it aborts.
    xhr.onerror = settle(() => reject(new TypeError('The server could not be reached.')));
    xhr.ontimeout = xhr.onerror;
    xhr.onabort = settle(() => reject((signal?.reason as Error | undefined) ?? new DOMException('The upload was aborted.', 'AbortError')));
    xhr.send(body);
  });
}

/** The headers of an answer, from XMLHttpRequest's lines of them. */
function responseHeaders(lines: string): Headers {
  const headers = new Headers();
  for (const line of lines.trim().split(/[\r\n]+/)) {
    const colon = line.indexOf(':');
    if (colon > 0) headers.append(line.slice(0, colon).trim(), line.slice(colon + 1).trim());
  }
  return headers;
}

/** Throws the server's error unless `response` succeeded. */
export async function throwIfFailed(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  let body: ErrorBody | null = null;
  try {
    body = (await response.json()) as ErrorBody;
  } catch {
    // Not a JSON error body, such as a proxy's error page.
  }

  throw toApiError(body, response);
}

function toApiError(error: unknown, response: Response): ApiError {
  const body = typeof error === 'object' && error !== null ? (error as ErrorBody) : null;
  return new ApiError(body?.message ?? `The server answered ${response.status} ${response.statusText}.`, response.status, body?.code ?? null, askAgainSecondsOf(response));
}

function askAgainSecondsOf(response: Response): number | null {
  const header = response.headers.get('Retry-After');
  if (header === null) return null;
  // Not as a date, which a proxy may write it as.
  return /^\d+$/.test(header) ? Number(header) : ASK_AGAIN_SECONDS;
}

export async function unwrapResponse<T>(promise: Promise<ApiFetchResult<T>>): Promise<T> {
  const { data, error, response } = await promise;
  if (error !== undefined || !response.ok) {
    throw toApiError(error, response);
  }

  if (data === undefined || data === null) {
    throw new ApiError('The server answered without a body.', response.status, null);
  }

  return data;
}

export async function unwrapNoContentResponse(promise: Promise<ApiFetchResult<unknown>>): Promise<void> {
  const { error, response } = await promise;
  if (error !== undefined || !response.ok) {
    throw toApiError(error, response);
  }
}
