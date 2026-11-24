import { CustomScroll } from '../../../ui';
import { useLoaded } from '../../../hooks/useLoaded';
import { formatBytes } from '../../../lib/format';
import { charsetOf } from '../../../lib/mediaTypes';
import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { LoadedContent } from '../LoadedContent';

/** How much of a text file is shown. */
const SHOWN_BYTES = 1024 * 1024;

/** A file's text as it is written, never rendered as what it describes: HTML shows as its source. */
export function TextViewer({ file, content }: ViewerProps) {
  const loaded = useLoaded(content, async (signal) => {
    // One byte past what is shown says whether there is more.
    const start = await content.range(0, SHOWN_BYTES + 1, signal);
    const cut = start.size > SHOWN_BYTES;
    const bytes = new Uint8Array(await start.slice(0, SHOWN_BYTES).arrayBuffer());
    // Streaming leaves out a character the cut went through, rather than showing it broken.
    return { text: decoderOf(bytes, file.contentType).decode(bytes, { stream: cut }), cut };
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

/**
 * Decodes a text in the encoding its byte order mark names, or else its stored type, as a browser's tab
 * does; UTF-8 when neither names one known.
 */
function decoderOf(bytes: Uint8Array, contentType: string): TextDecoder {
  const marked = markedEncoding(bytes);
  if (marked) return new TextDecoder(marked);
  try {
    return new TextDecoder(charsetOf(contentType));
  } catch {
    return new TextDecoder();
  }
}

/** The encoding a text's byte order mark names; null without one. */
function markedEncoding(bytes: Uint8Array): string | null {
  const starts = (...mark: number[]) => mark.every((byte, index) => bytes[index] === byte);
  if (starts(0xff, 0xfe)) return 'utf-16le';
  if (starts(0xfe, 0xff)) return 'utf-16be';
  return starts(0xef, 0xbb, 0xbf) ? 'utf-8' : null;
}
