// What plugins import as `@byteshelf/plugin`: the UI library, and what of the app they may use. All of it
// is what a plugin's `api` promises (PluginApi.cs): taking anything away raises it (index.test.ts).

export * from '../ui';
export { definePlugin, type PluginDefinition } from './pluginDefinition';
export { ApiError, authorizedFetch, throwIfFailed } from '../services/apiClient';
export { invalidateResource } from '../lib/invalidate';
export type { InvalidationResource } from '../lib/invalidationMap';
export { toast } from './toast';
