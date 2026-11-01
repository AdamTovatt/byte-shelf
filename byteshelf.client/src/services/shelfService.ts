import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type ShelfInfo = components['schemas']['ShelfInfo'];
export type DirectoryListing = components['schemas']['DirectoryListing'];
export type ShelfFile = components['schemas']['ShelfFile'];
export type ShelfDirectory = components['schemas']['ShelfDirectory'];
export type TrashEntry = components['schemas']['TrashEntry'];
export type DownloadLink = components['schemas']['DownloadLink'];
export type FileSearchResults = components['schemas']['FileSearchResults'];

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

/** Sets a file's description; null or blank removes it. */
export function setDescription(shelf: string, id: string, description: string | null): Promise<ShelfFile> {
  return unwrapResponse(apiClient.PUT('/api/shelves/{shelf}/files/description', { params: { ...shelfPath(shelf), query: { id } }, body: { description } }));
}

/** The first page of the live files under `under` whose name or description holds `text`, by path. */
export function searchFiles(shelf: string, under: string, text: string): Promise<FileSearchResults> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/search', { params: { ...shelfPath(shelf), query: { under, text } } }));
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

export function restoreDeletion(shelf: string, deletionId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.POST('/api/shelves/{shelf}/trash/{deletionId}/restore', { params: { path: { shelf, deletionId } } }));
}

export function emptyTrash(shelf: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/trash', { params: shelfPath(shelf) }));
}

/** A short-lived link to a file's contents that needs no token, for a browser tab or a download. */
export function createDownloadLink(shelf: string, id: string): Promise<DownloadLink> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/download-link', { params: { ...shelfPath(shelf), query: { id } } }));
}
