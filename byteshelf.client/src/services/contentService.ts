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
 * Whether a file is opened, saved and previewed from a blob of its contents read here rather than by
 * its address: an encrypted one, decrypted only in the page, and any file where the page cannot keep
 * the session's cookie (`opensByNavigation`).
 */
export function isBlobBacked(file: ShelfFile): boolean {
  return file.encrypted || !opensByNavigation();
}

/**
 * Where a file's contents are read from, opened and saved: the shelf as it is (`liveContent`), or one
 * deletion in its trash (`deletedContent`), which gives a file back without restoring it.
 */
export interface ContentSource {
  /** Where a navigation opens the file, or saves it with `download`, as `navigationUrl` says. */
  navigationUrl: (id: string, download: boolean) => Promise<string>;
  /** The file's stored bytes, with the session's token in the header; `signal` aborts the download. */
  read: (id: string, signal?: AbortSignal) => Promise<Blob>;
  /** The file's stored bytes from `start` up to, not including, `end` (or its end when that comes first). */
  readRange: (id: string, start: number, end: number, signal?: AbortSignal) => Promise<Blob>;
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
    ...readsOf((id) => contentUrl(shelf, id)),
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
    ...readsOf((id) => apiUrl('/api/shelves/{shelf}/trash/{deletionId}/content', { shelf, deletionId }, contentQuery(id, false))),
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

/** Where a live file's contents are read. */
function contentUrl(shelf: string, id: string): string {
  return apiUrl('/api/shelves/{shelf}/content', { shelf }, contentQuery(id, false));
}

/** The query naming a file's contents, and asking to save them rather than show them with `download`. */
function contentQuery(id: string, download: boolean): URLSearchParams {
  const query = new URLSearchParams({ id });
  if (download) query.set('download', 'true');
  return query;
}

/** The reads of files served at `urlOf`, with the session's token in the header. */
function readsOf(urlOf: (id: string) => string): Pick<ContentSource, 'read' | 'readRange'> {
  return {
    read: async (id, signal) => (await fetchBytes(urlOf(id), { signal })).blob(),
    readRange: async (id, start, end, signal) => {
      if (end <= start) return new Blob([]);
      const response = await authorizedFetch(urlOf(id), { method: 'GET', headers: { Range: `bytes=${start}-${end - 1}` }, signal });
      // The server answers that a range starts past the contents' end, as any range of an empty file does.
      if (response.status === 416) return new Blob([]);
      await throwIfFailed(response);
      const answered = await response.blob();
      return response.status === 206 ? answered : answered.slice(start, end);
    },
  };
}

/** An encrypted file's contents read from a source and decrypted; null when no password was given. */
export type DecryptedContents = (file: ShelfFile, source: ContentSource, signal?: AbortSignal) => Promise<Blob | null>;

/** A source's files, and a file's whole contents read from it into the page. */
export interface FileReading {
  source: ContentSource;
  /** The whole contents, decrypted when encrypted; null when no password was given. */
  contents: (file: ShelfFile, signal?: AbortSignal) => Promise<Blob | null>;
}

/** Reads files from `source`, an encrypted one decrypted by `decrypted`. */
export function fileReading(source: ContentSource, decrypted: DecryptedContents): FileReading {
  return {
    source,
    contents: (file, signal) => (file.encrypted ? decrypted(file, source, signal) : source.read(file.id, signal)),
  };
}

/**
 * A file's stored bytes as a stream, read only as far as the caller reads; `signal` aborts the
 * download, so a caller that needs only the start stops it there.
 */
export async function openContent(shelf: string, id: string, signal: AbortSignal): Promise<ReadableStream<Uint8Array>> {
  const response = await fetchBytes(contentUrl(shelf, id), { signal });
  if (!response.body) throw new Error('The server sent no contents.');
  return response.body;
}

async function fetchBytes(url: string, init: RequestInit): Promise<Response> {
  const response = await authorizedFetch(url, { method: 'GET', ...init });
  await throwIfFailed(response);
  return response;
}
