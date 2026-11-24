// A file's contents, as the web UI reads, opens and saves them. Never through a public link: a
// navigation (a tab, a save) or an element that loads a file (an image, a player, a frame) carries the
// session's token in a cookie the server sets at sign-in and renewal, and a fetch carries it in the header.

import { apiUrl, authorizedFetch, throwIfFailed } from './apiClient';
import { hasFreshCookie } from './authService';
import { fetchEncryptedFolder, type EncryptedFolder } from './encryptionService';
import type { DeletionTree, ShelfFile } from './shelfService';

/**
 * Whether this page opens, saves and previews a file by its address, its token in the session's cookie.
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
  /**
   * The file's address, where a navigation opens it, or saves it with `download`, and an element loads it:
   * the session's cookie, renewed first when it must be, carries its token.
   */
  navigationUrl: (id: string, download: boolean) => Promise<string>;
  /** The file's stored bytes, with the session's token in the header; `signal` aborts the download. */
  read: (id: string, signal?: AbortSignal) => Promise<Blob>;
  /** The file's stored bytes from `start` up to, not including, `end` (or its end when that comes first). */
  readRange: (id: string, start: number, end: number, signal?: AbortSignal) => Promise<Blob>;
  /**
   * The file's stored bytes as a stream, read only as far as the caller reads; `signal` aborts the
   * download, so a caller that needs only the start stops it there.
   */
  open: (id: string, signal: AbortSignal) => Promise<ReadableStream<Uint8Array>>;
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
    ...readsOf((id) => apiUrl('/api/shelves/{shelf}/content', { shelf }, contentQuery(id, false))),
    encryptedFolderOf: (file) => fetchEncryptedFolder(shelf, file.path),
  };
}

/**
 * The files one deletion took from the shelf. Their encrypted folder is the one they were stored in, as
 * the deletion's listing names it (`fetchDeletion`): it may have been moved or deleted since.
 */
export function deletedContent(shelf: string, deletionId: string, encryptedBy: EncryptedFolder | null): ContentSource {
  return {
    navigationUrl: async (id, download) => {
      await requireFreshCookie();
      return apiUrl('/api/open/{shelf}/trash/{deletionId}/content', { shelf, deletionId }, contentQuery(id, download));
    },
    ...readsOf((id) => apiUrl('/api/shelves/{shelf}/trash/{deletionId}/content', { shelf, deletionId }, contentQuery(id, false))),
    encryptedFolderOf: () => Promise.resolve(encryptedBy),
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

/** The reads of files served at `urlOf`, with the session's token in the header. */
function readsOf(urlOf: (id: string) => string): Pick<ContentSource, 'read' | 'readRange' | 'open'> {
  return {
    read: async (id, signal) => (await fetchBytes(urlOf(id), { signal })).blob(),
    readRange: async (id, start, end, signal) => {
      if (end <= start) return new Blob([]);
      const response = await requestBytes(urlOf(id), { headers: { Range: `bytes=${start}-${end - 1}` }, signal });
      // The server answers that a range starts past the contents' end, as any range of an empty file does.
      if (response.status === 416) return new Blob([]);
      await throwIfFailed(response);
      const answered = await response.blob();
      return response.status === 206 ? answered : answered.slice(start, end);
    },
    open: async (id, signal) => {
      const response = await fetchBytes(urlOf(id), { signal });
      if (!response.body) throw new Error('The server sent no contents.');
      return response.body;
    },
  };
}

/** Encrypted files of any source, opened with the passwords this tab keeps. */
export interface Decryption {
  /** The file's contents, decrypted with the password kept for it; null when none kept opens it. Asks for none. */
  read: (file: ShelfFile, source: ContentSource, signal?: AbortSignal) => Promise<Blob | null>;
  /**
   * Whether a password that opens the file is kept, asked for when none is: false when it was not given.
   * `signal` takes the prompt back.
   */
  unlock: (file: ShelfFile, source: ContentSource, signal?: AbortSignal) => Promise<boolean>;
}

/** A source's files, and a file's whole contents read from it into the page. */
export interface FileReading {
  source: ContentSource;
  /** The whole contents, decrypted when encrypted; null when it is locked. Asks for no password. */
  read: (file: ShelfFile, signal?: AbortSignal) => Promise<Blob | null>;
  /** Whether a locked file's password is given when asked for, so it can be read; `signal` takes the prompt back. */
  unlock: (file: ShelfFile, signal?: AbortSignal) => Promise<boolean>;
}

/** A file and what reads it. */
export interface ReadFile {
  file: ShelfFile;
  reading: FileReading;
}

/** Reads files from `source`, an encrypted one with `decryption`. */
export function fileReading(source: ContentSource, decryption: Decryption): FileReading {
  return {
    source,
    read: (file, signal) => (file.encrypted ? decryption.read(file, source, signal) : source.read(file.id, signal)),
    unlock: (file, signal) => decryption.unlock(file, source, signal),
  };
}

/**
 * The one file a file deletion took, read from it; null for a deletion of a folder, which is none of the
 * files it took.
 */
export function deletedFile(shelf: string, { deletion, tree, encryptedBy }: DeletionTree, decryption: Decryption): ReadFile | null {
  const file = tree.files.find((taken) => taken.id === deletion.entryId);
  return file ? { file, reading: fileReading(deletedContent(shelf, deletion.deletionId, encryptedBy ?? null), decryption) } : null;
}

/** A file's whole contents, its password asked for when it is locked; null when it was not given. */
export async function unlockedContents({ read, unlock }: Pick<FileReading, 'read' | 'unlock'>, file: ShelfFile): Promise<Blob | null> {
  return (await read(file)) ?? ((await unlock(file)) ? read(file) : null);
}

async function fetchBytes(url: string, init: RequestInit): Promise<Response> {
  const response = await requestBytes(url, init);
  await throwIfFailed(response);
  return response;
}

/**
 * Asks for a file's stored bytes past the browser's cache. Its contents may have been swapped at the same
 * address, and Chrome, asking whether a part it keeps still holds, hands on the same part of the new
 * contents as the whole.
 */
function requestBytes(url: string, init: RequestInit): Promise<Response> {
  return authorizedFetch(url, { method: 'GET', cache: 'no-store', ...init });
}
