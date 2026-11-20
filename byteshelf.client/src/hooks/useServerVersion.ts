import { useQuery } from '@tanstack/react-query';
import { fetchServerVersion } from '../services/versionService';
import { queryKeys } from '../lib/queryKeys';

/**
 * The release the server runs, for a signed-in user. Read once: it changes only with a deploy, and
 * useNewVersionAvailable offers a reload then, which reads it again.
 */
export function useServerVersion() {
  return useQuery({
    queryKey: queryKeys.version.release,
    queryFn: fetchServerVersion,
    staleTime: Infinity,
  });
}
