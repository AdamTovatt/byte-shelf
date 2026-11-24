import { BUILT_IN_VIEWERS } from '../components/preview/viewers';
import type { ViewerRegistry } from '../lib/preview/viewerRegistry';

/** The viewers files are previewed with: the one way the app reads them. */
export function useViewers(): ViewerRegistry {
  return BUILT_IN_VIEWERS;
}
