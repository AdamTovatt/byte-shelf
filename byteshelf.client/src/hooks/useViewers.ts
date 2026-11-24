import { useMemo } from 'react';
import { BUILT_IN_VIEWERS } from '../components/preview/viewers';
import type { ViewerRegistry } from '../lib/preview/viewerRegistry';
import { usePluginHost } from './usePluginHost';

/** The viewers files are previewed with, the plugins' loaded so far included: the one way the app reads them. */
export function useViewers(): ViewerRegistry {
  const { plugins } = usePluginHost();
  return useMemo(() => plugins.reduce((registry, { id, viewers }) => registry.with(id, viewers), BUILT_IN_VIEWERS), [plugins]);
}
