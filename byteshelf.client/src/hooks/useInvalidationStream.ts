import { useEffect } from 'react';
import { invalidateResource } from '../lib/invalidate';
import { INVALIDATION_RESOURCES } from '../lib/invalidationMap';
import { followInvalidations } from '../services/invalidationStream';

function invalidateAllResources(): void {
  INVALIDATION_RESOURCES.forEach(invalidateResource);
}

/** While `enabled`, refetches what each change the server publishes makes stale, and everything after a dropped stream. */
export function useInvalidationStream(enabled: boolean = true): void {
  useEffect(() => {
    if (!enabled) return undefined;
    return followInvalidations({ onResource: invalidateResource, onMissed: invalidateAllResources });
  }, [enabled]);
}
