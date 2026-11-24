import { useQuery } from '@tanstack/react-query';
import { fetchPlugins } from '../services/pluginService';
import { queryKeys } from '../lib/queryKeys';

/** The server's plugins, for a signed-in user. */
export function usePlugins(enabled = true) {
  return useQuery({ queryKey: queryKeys.plugins.list, queryFn: fetchPlugins, enabled });
}
