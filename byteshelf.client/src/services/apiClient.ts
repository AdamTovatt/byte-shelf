import createClient from 'openapi-fetch';
import type { paths } from '../types/api';
import { getFreshToken, renewAccessToken } from './authService';

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

/** After how many seconds to ask again for what the server is still making; null when the error is another. The answer to a HEAD has no body to tell it by a code. */
export function notReadyFor(error: unknown): number | null {
  return error instanceof ApiError && error.status === 503 ? error.askAgainSeconds : null;
}

/** Whether the server refused because what was asked for is not there. */
export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
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
 * made with authorizedFetch, so their paths are checked against the API description as apiClient's are.
 */
export function apiUrl<P extends keyof paths>(template: P, params: Record<PathParams<P>, string>, query?: URLSearchParams): string {
  const path = template.replace(/\{(\w+)\}/g, (_, name: PathParams<P>) => encodeURIComponent(params[name]));
  return query ? `${path}?${query}` : path;
}

/** For requests openapi-fetch does not suit, such as sending a file's bytes. */
export function authorizedFetch(input: string, init: RequestInit): Promise<Response> {
  return sendAuthorized(new Request(input, init), (request) => fetch(request));
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
