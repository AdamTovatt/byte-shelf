import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { UrlContent } from '../UrlContent';
import { playerFailure } from '../showFailure';

/** A recording in the browser's player. */
export function AudioViewer({ file, content }: ViewerProps) {
  return (
    <UrlContent content={content} cannotShow="This browser cannot play this recording.">
      {(src, onError) => (
        <div className="flex min-h-0 flex-1 items-center justify-center p-5">
          <audio src={src} aria-label={file.name} controls onError={(event) => onError(playerFailure(event.currentTarget))} className="w-full max-w-lg" />
        </div>
      )}
    </UrlContent>
  );
}
