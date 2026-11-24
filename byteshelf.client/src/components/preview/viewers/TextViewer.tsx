import { useLoaded } from '../../../hooks/useLoaded';
import { readText } from '../../../lib/content/readText';
import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { LoadedContent } from '../LoadedContent';
import { PlainText } from '../TextPane';

/** A file's text as it is written, never rendered as what it describes: HTML shows as its source. */
export function TextViewer({ file, content }: ViewerProps) {
  const loaded = useLoaded(content, (signal) => readText({ file, content }, signal));

  return <LoadedContent loaded={loaded}>{(read) => <PlainText {...read} />}</LoadedContent>;
}
