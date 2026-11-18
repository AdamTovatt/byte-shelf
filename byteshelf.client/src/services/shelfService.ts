import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type ShelfInfo = components['schemas']['ShelfInfo'];
export type DirectoryListing = components['schemas']['DirectoryListing'];
export type ShelfFile = components['schemas']['ShelfFile'];
export type ShelfDirectory = components['schemas']['ShelfDirectory'];
export type ListedDirectory = components['schemas']['ListedDirectory'];
export type TrashEntry = components['schemas']['TrashEntry'];
export type DeletionTree = components['schemas']['DeletionTree'];
export type FileSearchResults = components['schemas']['FileSearchResults'];
export type DuplicateSearchResults = components['schemas']['DuplicateSearchResults'];
export type DuplicateGroup = components['schemas']['DuplicateGroup'];
export type FileMetadata = components['schemas']['FileMetadata'];

/**
 * A directory or a file in a listing, as the shelf page and its dialogs act on it. A directory
 * carries the size and count of the files under it, and how many of them are encrypted, as its
 * listing gives them.
 */
export type Entry =
  | { kind: 'directory'; value: ShelfDirectory; sizeBytes: number; fileCount: number; encryptedFileCount: number }
  | { kind: 'file'; value: ShelfFile };

/** A listed directory as an entry. */
export function directoryEntry(listed: ListedDirectory): Entry {
  const { directory, sizeBytes, fileCount, encryptedFileCount } = listed;
  return { kind: 'directory', value: directory, sizeBytes, fileCount, encryptedFileCount };
}

/** What several entries are called in a title or a toast: the one's name, or how many there are. */
export function nameOrCount(entries: readonly Entry[]): string {
  return entries.length === 1 ? entries[0].value.name : `${entries.length} items`;
}

/** An entry's size: a file's own, or a directory's with everything under it. */
export function entrySizeBytes(entry: Entry): number {
  return entry.kind === 'file' ? entry.value.sizeBytes : entry.sizeBytes;
}

function shelfPath(shelf: string) {
  return { path: { shelf } };
}

export function fetchShelves(): Promise<ShelfInfo[]> {
  return unwrapResponse(apiClient.GET('/api/shelves'));
}

export function fetchShelf(shelf: string): Promise<ShelfInfo> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}', { params: shelfPath(shelf) }));
}

export function createShelf(name: string, quotaBytes: number | null): Promise<ShelfInfo> {
  return unwrapResponse(apiClient.POST('/api/shelves', { body: { name, quotaBytes } }));
}

export function updateShelf(shelf: string, name: string, quotaBytes: number | null): Promise<ShelfInfo> {
  return unwrapResponse(apiClient.PUT('/api/shelves/{shelf}', { params: shelfPath(shelf), body: { name, quotaBytes } }));
}

export function deleteShelf(shelf: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}', { params: shelfPath(shelf) }));
}

export function fetchListing(shelf: string, path: string): Promise<DirectoryListing> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/entries', { params: { ...shelfPath(shelf), query: { path } } }));
}

export function createDirectory(shelf: string, path: string): Promise<ShelfDirectory> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/directories', { params: shelfPath(shelf), body: { path } }));
}

export function moveFile(shelf: string, id: string, toPath: string): Promise<ShelfFile> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/files/move', { params: { ...shelfPath(shelf), query: { id } }, body: { toPath } }));
}

export function moveDirectory(shelf: string, id: string, toPath: string): Promise<ShelfDirectory> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/directories/move', { params: { ...shelfPath(shelf), query: { id } }, body: { toPath } }));
}

/** What the server read from a file's contents; `readAt` is null until it has, and for good when `readable` is false (an encrypted file, or a type it does not read). */
export function fetchFileMetadata(shelf: string, id: string): Promise<FileMetadata> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/files/metadata', { params: { ...shelfPath(shelf), query: { id } } }));
}

/** Sets a file's description; null or blank removes it. */
export function setDescription(shelf: string, id: string, description: string | null): Promise<ShelfFile> {
  return unwrapResponse(apiClient.PUT('/api/shelves/{shelf}/files/description', { params: { ...shelfPath(shelf), query: { id } }, body: { description } }));
}

/** The first page of the live files under `under` whose name or description holds `text`, by path. */
export function searchFiles(shelf: string, under: string, text: string): Promise<FileSearchResults> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/search', { params: { ...shelfPath(shelf), query: { under, text } } }));
}

/**
 * A page of the groups of plain files under `under` with the same contents, most repeated bytes
 * first: the first, or the one after the cursor `after`. With `text`, only files whose name or
 * description holds it count.
 */
export function findDuplicates(shelf: string, under: string, text: string, after?: string): Promise<DuplicateSearchResults> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/duplicates', { params: { ...shelfPath(shelf), query: { under, text: text || undefined, after } } }));
}

export function deleteFile(shelf: string, id: string): Promise<TrashEntry> {
  return unwrapResponse(apiClient.DELETE('/api/shelves/{shelf}/files', { params: { ...shelfPath(shelf), query: { id } } }));
}

export function deleteDirectory(shelf: string, id: string): Promise<TrashEntry> {
  return unwrapResponse(apiClient.DELETE('/api/shelves/{shelf}/directories', { params: { ...shelfPath(shelf), query: { id } } }));
}

export function fetchTrash(shelf: string): Promise<TrashEntry[]> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/trash', { params: shelfPath(shelf) }));
}

/**
 * What a deletion took and the encrypted folder its files were in, with the first page of it: all of
 * a file deletion, the one file.
 */
export function fetchDeletion(shelf: string, deletionId: string): Promise<DeletionTree> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/trash/{deletionId}/tree', { params: { path: { shelf, deletionId }, query: { limit: 1 } } }));
}

export function restoreDeletion(shelf: string, deletionId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.POST('/api/shelves/{shelf}/trash/{deletionId}/restore', { params: { path: { shelf, deletionId } } }));
}

export function emptyTrash(shelf: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/trash', { params: shelfPath(shelf) }));
}
