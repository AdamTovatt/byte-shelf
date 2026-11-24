import { useZoom } from '../../../hooks/useZoom';
import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { UrlContent } from '../UrlContent';

/**
 * An image fitted to the preview: the wheel, a pinch or a double-click zooms, a drag moves it. Always an
 * `<img>`, where an SVG's scripts do not run.
 */
export function ImageViewer({ file, content }: ViewerProps) {
  return (
    <UrlContent content={content} cannotShow="This browser cannot show this image.">
      {(src, onError) => <ZoomableImage src={src} name={file.name} onError={() => onError('cannotShow')} />}
    </UrlContent>
  );
}

function ZoomableImage({ src, name, onError }: { src: string; name: string; onError: () => void }) {
  const { frame, zoomed } = useZoom<HTMLImageElement>();
  return (
    <div {...frame}>
      <img {...zoomed} src={src} alt={name} draggable={false} onError={onError} className="max-h-full max-w-full object-contain" />
    </div>
  );
}
