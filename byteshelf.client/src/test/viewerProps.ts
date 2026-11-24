import type { ContentReader } from '../lib/content/contentReader';
import type { ViewerProps } from '../lib/preview/viewerRegistry';
import type { ShelfFile } from '../services/shelfService';

/** What a viewer is given of a file nothing was made from, with none beside it. */
export function viewerProps(file: Partial<ShelfFile>, content: ContentReader): ViewerProps {
  return { file: file as ShelfFile, content, derived: { has: () => false, url: async () => null, forget: () => {} }, neighbours: [] };
}
