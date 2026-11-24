import type { ShelfFile } from '../../services/shelfService';
import { charsetOf } from '../mediaTypes';
import type { ContentReader } from './contentReader';

/** How much of a text file is shown. */
export const SHOWN_TEXT_BYTES = 1024 * 1024;

/** A file's text, or the start of it. */
export interface ReadText {
  text: string;
  /** How many bytes of the file it is, when there are more; null when it is all. */
  cutAt: number | null;
}

/** A file's text, up to `SHOWN_TEXT_BYTES` of it. */
export async function readText({ file, content }: { file: ShelfFile; content: ContentReader }, signal?: AbortSignal): Promise<ReadText> {
  // One byte past what is shown says whether there is more.
  const start = await content.range(0, SHOWN_TEXT_BYTES + 1, signal);
  const cut = start.size > SHOWN_TEXT_BYTES;
  const bytes = new Uint8Array(await start.slice(0, SHOWN_TEXT_BYTES).arrayBuffer());
  // Streaming leaves out a character the cut went through, rather than showing it broken.
  return { text: decoderOf(bytes, file.contentType).decode(bytes, { stream: cut }), cutAt: cut ? SHOWN_TEXT_BYTES : null };
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
