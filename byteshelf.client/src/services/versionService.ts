import { apiClient, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type VersionView = components['schemas']['VersionView'];

export async function fetchCurrentVersion(): Promise<VersionView> {
  return unwrapResponse(apiClient.GET('/api/version/current'));
}
