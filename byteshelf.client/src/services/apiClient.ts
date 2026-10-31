import createClient from 'openapi-fetch';
import type { paths } from '../types/api';
import { getFreshToken, hasSession, redirectToLoginIfNeeded, renewAccessToken } from './authService';

/** A refusal from the server, with the machine-readable code from its error body when it gave one. */
export class ApiError extends Error {
  public status: number;
  public code: string | null;

  constructor(message: string, status: number, code: string | null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
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

  const renewed = await renewAccessToken(token);
  if (!renewed) {
    // renewAccessToken forgets a session the server ended; one it could not renew just now, such as
    // while the server is unreachable, is kept for the next request.
    if (!hasSession()) redirectToLoginIfNeeded();
    return response;
  }

  retry.headers.set('Authorization', `Bearer ${renewed}`);
  return send(retry);
}

export const apiClient = createClient<paths>({
  baseUrl: '',
  fetch: (request: Request) => sendAuthorized(request, (sent) => fetch(sent)),
});

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
  return new ApiError(body?.message ?? `The server answered ${response.status} ${response.statusText}.`, response.status, body?.code ?? null);
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
