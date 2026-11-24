import { apiClient, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type PluginList = components['schemas']['PluginList'];
export type PluginView = components['schemas']['PluginView'];
export type SkippedPlugin = components['schemas']['SkippedPlugin'];

/** The plugins the server loaded and, for an admin, those it skipped. */
export function fetchPlugins(): Promise<PluginList> {
  return unwrapResponse(apiClient.GET('/api/plugins'));
}
