import { useEffect, useSyncExternalStore } from 'react';
import { pluginHost, type PluginHostState } from '../lib/plugins/pluginHost';
import { usePlugins } from './usePlugins';

/** What the plugins loaded so far add, and which failed. */
export function usePluginHost(): PluginHostState {
  return useSyncExternalStore(pluginHost.subscribe, pluginHost.state);
}

/** Loads the plugins' parts in the web UI, once the server has told a signed-in user which there are. */
export function useLoadPlugins(isSignedIn: boolean): void {
  const { data } = usePlugins(isSignedIn);

  useEffect(() => {
    if (data) void pluginHost.load(data.plugins);
  }, [data]);
}
