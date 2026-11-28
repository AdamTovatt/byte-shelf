import { useEffect, type ReactNode } from 'react';
import { ViewerPreviewProvider, useViewerPreviewHost } from '../lib/preview/viewerPreview';

/**
 * For plugins' tests (`@byteshelf/plugin/testing`): a preview a viewer is drawn in, as the only overlay. Every key
 * pressed in the document is its (`usePreviewKeys`), but Escape and Tab, which an overlay keeps, and its controls are
 * drawn after it (`PreviewControls`).
 */
export function InPreview({ children }: { children: ReactNode }) {
  const { preview, setSlot, offer } = useViewerPreviewHost();

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' && event.key !== 'Tab') offer(event);
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [offer]);

  return (
    <div>
      <ViewerPreviewProvider value={preview}>{children}</ViewerPreviewProvider>
      <div ref={setSlot} />
    </div>
  );
}
