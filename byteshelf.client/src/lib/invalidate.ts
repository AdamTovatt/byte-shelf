import { INVALIDATION_RESOURCES, RESOURCE_TO_QUERY_KEYS, type InvalidationResource } from './invalidationMap';
import { queryClient } from './queryClient';

/**
 * Makes everything read of `resource` stale: what is on screen is read again at once, the rest when it is
 * next shown. One this page does not know does nothing: it has nothing of it. A newer server publishes
 * such ones, and a plugin may name one.
 */
export function invalidateResource(resource: InvalidationResource | (string & {})): void {
  const known = INVALIDATION_RESOURCES.find((each) => each === resource);
  for (const queryKey of known ? RESOURCE_TO_QUERY_KEYS[known] : []) {
    void queryClient.invalidateQueries({ queryKey });
  }
}
