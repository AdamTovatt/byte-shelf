import type { ReadText } from '@byteshelf/plugin';

/** How much of a document is shown as it reads: working it out holds the page still, about a second for this much. */
export const RENDERED_BYTES = 128 * 1024;

/** The start of a text that is shown as it reads. */
export function renderedStart({ text, cutAt }: ReadText): ReadText {
  const { read } = new TextEncoder().encodeInto(text, new Uint8Array(RENDERED_BYTES));
  return read < text.length ? { text: text.slice(0, read), cutAt: RENDERED_BYTES } : { text, cutAt };
}
