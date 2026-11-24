import { CustomScroll } from '../../../ui';
import { useLoaded } from '../../../hooks/useLoaded';
import { formatBytes } from '../../../lib/format';
import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { LoadedContent } from '../LoadedContent';

/** How much of a text file is shown. */
const SHOWN_BYTES = 1024 * 1024;

/** A file's text as it is written, never rendered as what it describes: HTML shows as its source. */
export function TextViewer({ content }: ViewerProps) {
  const loaded = useLoaded(content, async (signal) => {
    // One byte past what is shown says whether there is more.
    const start = await content.range(0, SHOWN_BYTES + 1, signal);
    const cut = start.size > SHOWN_BYTES;
    const bytes = new Uint8Array(await start.slice(0, SHOWN_BYTES).arrayBuffer());
    // Streaming leaves out a character the cut went through, rather than showing it broken.
    return { text: new TextDecoder(encodingOf(bytes)).decode(bytes, { stream: cut }), cut };
  });

  return (
    <LoadedContent loaded={loaded}>
      {({ text, cut }) => (
        <div className="flex min-h-0 flex-1 flex-col">
          {cut && <p className="border-b border-border px-5 py-2 text-body-sm text-muted">Only the first {formatBytes(SHOWN_BYTES)} is shown.</p>}
          <CustomScroll flex="1 1 auto" heightRelativeToParent="100%" className="min-h-0">
            <pre className="whitespace-pre-wrap break-words p-5 font-mono text-body-sm text-fg">{text}</pre>
          </CustomScroll>
        </div>
      )}
    </LoadedContent>
  );
}

/** The encoding a text's byte order mark names, as a browser's tab reads it; UTF-8 without one. */
function encodingOf(bytes: Uint8Array): string {
  if (bytes[0] === 0xff && bytes[1] === 0xfe) return 'utf-16le';
  if (bytes[0] === 0xfe && bytes[1] === 0xff) return 'utf-16be';
  return 'utf-8';
}
