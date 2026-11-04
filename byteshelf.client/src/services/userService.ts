import { apiClient, unwrapResponse, unwrapNoContentResponse } from './apiClient';
import type { components } from '../types/api';

export type UserInfo = components['schemas']['UserInfo'];
export type CreateUserRequest = components['schemas']['CreateUserRequest'];
export type UpdatePasswordRequest = components['schemas']['UpdatePasswordRequest'];

/** The one role there is: creates shelves and users, and reaches every shelf. */
export const ADMIN_ROLE = 'admin';

export function isAdmin(user: UserInfo | undefined): boolean {
  return user?.roles.includes(ADMIN_ROLE) ?? false;
}

export function fetchCurrentUser(): Promise<UserInfo> {
  return unwrapResponse(apiClient.GET('/api/users/me'));
}

export function fetchAllUsers(): Promise<UserInfo[]> {
  return unwrapResponse(apiClient.GET('/api/users'));
}

export function createUser(request: CreateUserRequest): Promise<UserInfo> {
  return unwrapResponse(apiClient.POST('/api/users', { body: request }));
}

export function setRoles(userId: string, roles: string[]): Promise<void> {
  return unwrapNoContentResponse(apiClient.PUT('/api/users/{userId}/roles', { params: { path: { userId } }, body: { roles } }));
}

export function deleteUser(userId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/users/{userId}', { params: { path: { userId } } }));
}

export function changeOwnPassword(request: UpdatePasswordRequest): Promise<void> {
  return unwrapNoContentResponse(apiClient.PUT('/api/users/me/password', { body: request }));
}
