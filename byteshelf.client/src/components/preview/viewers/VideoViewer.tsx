import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { UrlContent } from '../UrlContent';
import { playerFailure } from '../showFailure';

/** A video in the browser's player. */
export function VideoViewer({ file, content }: ViewerProps) {
  return (
    <UrlContent content={content} cannotShow="This browser cannot play this video.">
      {(src, onError) => (
        <div className="flex min-h-0 flex-1 items-center justify-center bg-black">
          <video src={src} aria-label={file.name} controls playsInline onError={(event) => onError(playerFailure(event.currentTarget))} className="max-h-full max-w-full" />
        </div>
      )}
    </UrlContent>
  );
}
