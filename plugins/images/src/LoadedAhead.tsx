import { useEffect } from 'react';
import { loadImage, usePluginDocuments, type Neighbour } from '@byteshelf/plugin';

/** Loads ahead what a neighbour is first shown with: its size, and its preview when there is one. */
export function LoadedAhead({ file, derived }: Neighbour) {
  usePluginDocuments(file);

  const hasPreview = derived.has('preview');
  useEffect(() => {
    if (!hasPreview) return;
    const loading = new AbortController();
    const image = new Image();
    image.fetchPriority = 'low';
    derived.url('preview', loading.signal)
      .then((url) => (url === null ? undefined : loadImage(image, url, loading.signal)))
      .catch(() => undefined);
    // Once what is shown next has had the time to go on with it.
    return () => void setTimeout(() => loading.abort());
  }, [derived, hasPreview]);

  return null;
}
