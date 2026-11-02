import { apiClient, unwrapResponse } from './apiClient';
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

/**
 * Swaps `file`'s contents in place for `content`, encrypted or not: it keeps its id, type and
 * description, and the old contents are gone at once. Refused when it changed since it was read.
 * Named here, beside the folder calls, so encrypting reaches the server through this one module
 * (and its tests stand in for that one module).
 */
export function replaceContent(shelf: string, file: ShelfFile, content: Blob, encrypted: boolean): Promise<ShelfFile> {
  return swapContent({ shelf, file, content, encrypted });
}

/** Every folder and file under the folder at `path`, at any depth, read whole before anything is changed. */
export async function treeUnder(shelf: string, path: string): Promise<{ directories: ShelfDirectory[]; files: ShelfFile[] }> {
  const directories: ShelfDirectory[] = [];
  const files: ShelfFile[] = [];
  let after: string | undefined;
  do {
    const page = await unwrapResponse(apiClient.GET('/api/shelves/{shelf}/tree', { params: { path: { shelf }, query: { under: path, limit: 1000, after } } }));
    directories.push(...page.directories);
    files.push(...page.files);
    after = page.nextCursor ?? undefined;
  } while (after);
  return { directories, files };
}

/** Every file under the folder at `path`, at any depth, read whole before anything is changed. */
export async function filesUnder(shelf: string, path: string): Promise<ShelfFile[]> {
  return (await treeUnder(shelf, path)).files;
}
