// A file's contents, as the web UI reads, opens and saves them. Never through a public link: a
// navigation (a tab, a save) carries the session's token in a cookie the server sets at sign-in and
// renewal, and a fetch carries it in the header.

import { apiUrl, authorizedFetch, throwIfFailed } from './apiClient';
import { hasFreshCookie } from './authService';

/**
 * Whether this page opens and saves a file by navigating to it, its token in the session's cookie.
 * That cookie is `Secure`, so a browser keeps it only over HTTPS or on localhost; anywhere else the
 * contents are fetched with the header and shown or saved from a blob, as an encrypted file's are.
 */
export function opensByNavigation(): boolean {
  return window.isSecureContext;
}

/**
 * Where a navigation opens a file (or saves it, with `download`), the session's cookie renewed first
 * when it may be missing or about to run out: the cookie lives exactly as long as the token it carries.
 */
export async function navigationUrl(shelf: string, id: string, download: boolean): Promise<string> {
  if (!(await hasFreshCookie())) throw new Error('The session has ended. Sign in again to open files.');
  const query = new URLSearchParams({ id });
  if (download) query.set('download', 'true');
  return apiUrl('/api/open/{shelf}/content', { shelf }, query);
}

/** A file's stored bytes, with the session's token in the header, typed as the server stores it. */
export async function readContent(shelf: string, id: string): Promise<Blob> {
  return (await fetchContent(shelf, id)).blob();
}

/**
 * A file's stored bytes as a stream, read only as far as the caller reads; `signal` aborts the
 * download, so a caller that needs only the start stops it there.
 */
export async function openContent(shelf: string, id: string, signal: AbortSignal): Promise<ReadableStream<Uint8Array>> {
  const response = await fetchContent(shelf, id, signal);
  if (!response.body) throw new Error('The server sent no contents.');
  return response.body;
}

async function fetchContent(shelf: string, id: string, signal?: AbortSignal): Promise<Response> {
  const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/content', { shelf }, new URLSearchParams({ id })), { method: 'GET', signal });
  await throwIfFailed(response);
  return response;
}
