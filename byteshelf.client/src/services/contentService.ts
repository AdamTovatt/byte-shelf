// A file's contents, as the web UI reads, opens and saves them. Never through a public link: a
// navigation (a tab, a save) carries the session's token in a cookie the server sets at sign-in and
// renewal, and a fetch carries it in the header.

import { apiUrl, authorizedFetch, throwIfFailed } from './apiClient';
import { hasFreshCookie } from './authService';
import { fetchEncryptedFolder, type EncryptedFolder } from './encryptionService';
import type { DeletionTree, ShelfFile } from './shelfService';

/**
 * Whether this page opens and saves a file by navigating to it, its token in the session's cookie.
 * That cookie is `Secure`, so a browser keeps it only over HTTPS or on localhost; anywhere else the
 * contents are fetched with the header and shown or saved from a blob, as an encrypted file's are.
 */
export function opensByNavigation(): boolean {
  return window.isSecureContext;
}

/**
 * Where a file's contents are read from, opened and saved: the shelf as it is (`liveContent`), or one
 * deletion in its trash (`deletedContent`), which gives a file back without restoring it.
 */
export interface ContentSource {
  /** Where a navigation opens the file, or saves it with `download`, as `navigationUrl` says. */
  navigationUrl: (id: string, download: boolean) => Promise<string>;
  /** The file's stored bytes, with the session's token in the header. */
  read: (id: string) => Promise<Blob>;
  /** The encrypted folder whose password opens an encrypted file; null when the file has its own. */
  encryptedFolderOf: (file: ShelfFile) => Promise<EncryptedFolder | null>;
}

/** The shelf's live files. */
export function liveContent(shelf: string): ContentSource {
  return {
    navigationUrl: async (id, download) => {
      await requireFreshCookie();
      return apiUrl('/api/open/{shelf}/content', { shelf }, contentQuery(id, download));
    },
    read: (id) => readContent(shelf, id),
    encryptedFolderOf: (file) => fetchEncryptedFolder(shelf, file.path),
  };
}

/**
 * The files one deletion took from the shelf, as its listing (`fetchDeletion`) describes them: their
 * encrypted folder is the one they were stored in, which may have been moved or deleted since.
 */
export function deletedContent(shelf: string, deletion: DeletionTree): ContentSource {
  const deletionId = deletion.deletion.deletionId;
  return {
    navigationUrl: async (id, download) => {
      await requireFreshCookie();
      return apiUrl('/api/open/{shelf}/trash/{deletionId}/content', { shelf, deletionId }, contentQuery(id, download));
    },
    read: async (id) => {
      const response = await authorizedFetch(apiUrl('/api/shelves/{shelf}/trash/{deletionId}/content', { shelf, deletionId }, contentQuery(id, false)), { method: 'GET' });
      await throwIfFailed(response);
      return response.blob();
    },
    encryptedFolderOf: () => Promise.resolve(deletion.encryptedBy ?? null),
  };
}

/**
 * Renews the session's cookie first when it may be missing or about to run out, before a navigation
 * that carries it: the cookie lives exactly as long as the token it carries.
 */
async function requireFreshCookie(): Promise<void> {
  if (!(await hasFreshCookie())) throw new Error('The session has ended. Sign in again to open files.');
}

/** The query naming a file's contents, and asking to save them rather than show them with `download`. */
function contentQuery(id: string, download: boolean): URLSearchParams {
  const query = new URLSearchParams({ id });
  if (download) query.set('download', 'true');
  return query;
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
