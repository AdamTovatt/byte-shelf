import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchCurrentVersion } from '../services/versionService';
import { queryKeys } from '../lib/queryKeys';

const POLL_INTERVAL_MS = 5 * 60_000;

/**
 * Reports true once the running build's version id differs from the one observed at app boot.
 * Once stale, polling stops — there's no remaining signal worth fetching.
 */
export function useNewVersionAvailable(): boolean {
  const [isStale, setIsStale] = useState(false);
  const bootVersion = useRef<string | null>(null);

  const { data } = useQuery({
    queryKey: queryKeys.version.current,
    queryFn: fetchCurrentVersion,
    refetchInterval: POLL_INTERVAL_MS,
    refetchOnWindowFocus: true,
    staleTime: 0,
    enabled: !isStale,
  });

  useEffect(() => {
    if (!data) return;
    if (bootVersion.current === null) {
      bootVersion.current = data.versionId;
      return;
    }
    if (data.versionId !== bootVersion.current) {
      setIsStale(true);
    }
  }, [data]);

  return isStale;
}
