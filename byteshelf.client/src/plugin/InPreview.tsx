import { useEffect, type ReactNode } from 'react';
import { ViewerPreviewProvider, useViewerPreviewHost } from '../lib/preview/viewerPreview';

/**
 * For plugins' tests (`@byteshelf/plugin/testing`): a preview a viewer is drawn in, as the only overlay. Every key
 * pressed in the document is its (`usePreviewKeys`), and its controls are drawn after it (`PreviewControls`).
 */
export function InPreview({ children }: { children: ReactNode }) {
  const { preview, setSlot, offer } = useViewerPreviewHost();

  useEffect(() => {
    document.addEventListener('keydown', offer);
    return () => document.removeEventListener('keydown', offer);
  }, [offer]);

  return (
    <div>
      <ViewerPreviewProvider value={preview}>{children}</ViewerPreviewProvider>
      <div ref={setSlot} />
    </div>
  );
}
