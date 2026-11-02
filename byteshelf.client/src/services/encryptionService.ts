import { apiClient, apiUrl, authorizedFetch, throwIfFailed, unwrapResponse } from './apiClient';
import type { components } from '../types/api';
import type { ShelfDirectory, ShelfFile } from './shelfService';
import { swapContent } from './uploadService';

export type EncryptedFolder = components['schemas']['EncryptedFolder'];

/** The encrypted folder a file at `path` is, or would be, under; null for none. */
export async function fetchEncryptedFolder(shelf: string, path: string): Promise<EncryptedFolder | null> {
  const answer = await unwrapResponse(apiClient.GET('/api/shelves/{shelf}/directories/encryption', { params: { path: { shelf }, query: { path } } }));
  return answer.encryptedBy ?? null;
}

/** Marks a folder encrypted with the password `check` was made with; its files are left to the caller. */
export function encryptDirectory(shelf: string, id: string, check: string): Promise<ShelfDirectory> {
  return unwrapResponse(apiClient.PUT('/api/shelves/{shelf}/directories/encryption', { params: { path: { shelf }, query: { id } }, body: { check } }));
}

/** Stops encrypting a folder; the files in it stay encrypted until the caller decrypts them. */
export function removeDirectoryEncryption(shelf: string, id: string): Promise<ShelfDirectory> {
  return unwrapResponse(apiClient.DELETE('/api/shelves/{shelf}/directories/encryption', { params: { path: { shelf }, query: { id } } }));
}

/** A file's stored bytes, with the session's token: an encrypted file never goes through a link. */
export async function readContent(shelf: string, id: string): Promise<Blob> {
  const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/content', { shelf }, new URLSearchParams({ id })), { method: 'GET' });
  await throwIfFailed(response);
  return response.blob();
}

/**
 * A file's stored bytes as a stream, read only as far as the caller reads; `signal` aborts the
 * download, so a caller that needs only the start stops it there.
 */
export async function openContent(shelf: string, id: string, signal: AbortSignal): Promise<ReadableStream<Uint8Array>> {
  const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/content', { shelf }, new URLSearchParams({ id })), { method: 'GET', signal });
  await throwIfFailed(response);
  if (!response.body) throw new Error('The server sent no contents.');
  return response.body;
}

/**
 * Swaps `file`'s contents in place for `content`, encrypted or not: it keeps its id, type and
 * description, and the old contents are gone at once. Refused when it changed since it was read.
 */
export function replaceContent(shelf: string, file: ShelfFile, content: Blob, encrypted: boolean): Promise<ShelfFile> {
  return swapContent({ shelf, file, content, encrypted });
}

/** Every file under the folder at `path`, at any depth, read whole before anything is changed. */
export async function filesUnder(shelf: string, path: string): Promise<ShelfFile[]> {
  const files: ShelfFile[] = [];
  let after: string | undefined;
  do {
    const page = await unwrapResponse(apiClient.GET('/api/shelves/{shelf}/tree', { params: { path: { shelf }, query: { under: path, limit: 1000, after } } }));
    files.push(...page.files);
    after = page.nextCursor ?? undefined;
  } while (after);
  return files;
}
