import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { LoadedContent } from '../LoadedContent';
import { useLoaded } from '../../../hooks/useLoaded';

/** A PDF in the browser's own viewer. */
export function PdfViewer({ file, content }: ViewerProps) {
  const url = useLoaded(content, (signal) => content.url(signal));
  return <LoadedContent loaded={url}>{(src) => <iframe src={src} title={file.name} className="min-h-0 w-full flex-1 border-0" />}</LoadedContent>;
}
