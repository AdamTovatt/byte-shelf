import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type ShelfAccessGrant = components['schemas']['ShelfAccessGrant'];
export type ShelfAccessPermission = components['schemas']['ShelfAccessPermission'];
export type ShelfKeyInfo = components['schemas']['ShelfKeyInfo'];
export type ShelfKeyPermission = components['schemas']['ShelfKeyPermission'];
export type CreatedShelfKey = components['schemas']['CreatedShelfKey'];

export function fetchGrants(shelf: string): Promise<ShelfAccessGrant[]> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/access', { params: { path: { shelf } } }));
}

export function setGrant(shelf: string, userId: string, permission: ShelfAccessPermission): Promise<ShelfAccessGrant> {
  return unwrapResponse(apiClient.PUT('/api/shelves/{shelf}/access/{userId}', { params: { path: { shelf, userId } }, body: { permission } }));
}

export function removeGrant(shelf: string, userId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/access/{userId}', { params: { path: { shelf, userId } } }));
}

export function fetchKeys(shelf: string): Promise<ShelfKeyInfo[]> {
  return unwrapResponse(apiClient.GET('/api/shelves/{shelf}/keys', { params: { path: { shelf } } }));
}

export function createKey(shelf: string, name: string, permission: ShelfKeyPermission): Promise<CreatedShelfKey> {
  return unwrapResponse(apiClient.POST('/api/shelves/{shelf}/keys', { params: { path: { shelf } }, body: { name, permission } }));
}

export function deleteKey(shelf: string, keyId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/shelves/{shelf}/keys/{keyId}', { params: { path: { shelf, keyId } } }));
}
