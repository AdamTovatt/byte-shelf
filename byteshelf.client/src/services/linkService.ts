import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type ShelfLink = components['schemas']['ShelfLink'];
export type DownloadLink = components['schemas']['DownloadLink'];

/** The shelf's share and upload links that have not run out: the caller's own, or every one when it may write there. */
export function fetchLinks(shelf: string): Promise<ShelfLink[]> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/links', { params: { path: { shelf } } }));
}

/**
 * A link to a file that works without signing in, for `expiresInMinutes` (the server's 24 hours when
 * left out) or until it is revoked. `url` is relative to the server's address.
 */
export function createDownloadLink(shelf: string, id: string, expiresInMinutes?: number): Promise<DownloadLink> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/download-link', { params: { path: { shelf }, query: { id, expiresInMinutes } } }));
}

/** Ends a link: nothing more is served through it. */
export function revokeLink(shelf: string, linkId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/links/{linkId}', { params: { path: { shelf, linkId } } }));
}
