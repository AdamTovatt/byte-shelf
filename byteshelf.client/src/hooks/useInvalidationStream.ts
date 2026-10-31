import { useEffect } from 'react';
import { queryClient } from '../lib/queryClient';
import { RESOURCE_TO_QUERY_KEYS } from '../lib/invalidationMap';
import { followInvalidations } from '../services/invalidationStream';

function invalidateResource(resource: string): void {
  for (const queryKey of RESOURCE_TO_QUERY_KEYS[resource] ?? []) {
    void queryClient.invalidateQueries({ queryKey });
  }
}

function invalidateAllResources(): void {
  for (const resource of Object.keys(RESOURCE_TO_QUERY_KEYS)) {
    invalidateResource(resource);
  }
}

/** While `enabled`, refetches what each change the server publishes makes stale, and everything after a dropped stream. */
export function useInvalidationStream(enabled: boolean = true): void {
  useEffect(() => {
    if (!enabled) return undefined;
    return followInvalidations({ onResource: invalidateResource, onMissed: invalidateAllResources });
  }, [enabled]);
}
