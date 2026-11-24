import { useEffect, useState, useSyncExternalStore } from 'react';
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

/** Whether each plugin has been loaded or has failed, or which there are cannot be told. */
export function usePluginsSettled(): boolean {
  const { data, isError } = usePlugins();
  const { plugins, failures } = usePluginHost();

  // By what the server lists, not by what is under way: loading starts only after the list is here.
  if (!data) return isError;
  return data.plugins.every(({ id, moduleUrl }) => !moduleUrl || failures.has(id) || plugins.some((plugin) => plugin.id === id));
}

/** How long something waits for plugins still loading. */
const PATIENCE_MS = 3_000;

/** Whether to wait for plugins that are still loading: until they are, at most `PATIENCE_MS`. */
export function usePluginsAwaited(): boolean {
  const settled = usePluginsSettled();
  const [isOver, setIsOver] = useState(settled);
  // For good: a plugin added later, as after a restart of the server, is not waited for.
  if (settled && !isOver) setIsOver(true);

  useEffect(() => {
    if (isOver) return undefined;
    const over = setTimeout(() => setIsOver(true), PATIENCE_MS);
    return () => clearTimeout(over);
  }, [isOver]);

  return !settled && !isOver;
}
