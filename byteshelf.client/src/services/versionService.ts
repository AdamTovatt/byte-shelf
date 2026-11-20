import { apiClient, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type VersionView = components['schemas']['VersionView'];
export type ServerVersion = components['schemas']['ServerVersion'];

export async function fetchCurrentVersion(): Promise<VersionView> {
  return unwrapResponse(apiClient.GET('/api/version/current'));
}

/** The release the server runs; only a signed-in user may read it, unlike the public deploy id. */
export async function fetchServerVersion(): Promise<ServerVersion> {
  return unwrapResponse(apiClient.GET('/api/version/release'));
}
