/**
 * Copies text that is still being fetched, such as a link the server is making. Call it inside the
 * click, before any await: Safari refuses a clipboard write that starts after one, so the write starts
 * at once with a promise of the text. Answers whether it was copied: false where the browser offers
 * no clipboard (it does only on HTTPS and localhost) or refuses. A failure to get the text also
 * answers false; the caller sees why from its own promise.
 */
export async function copyWhenReady(text: Promise<string>): Promise<boolean> {
  if (!navigator.clipboard) return false;

  try {
    if (typeof ClipboardItem !== 'undefined') {
      const blob = text.then((value) => new Blob([value], { type: 'text/plain' }));
      await navigator.clipboard.write([new ClipboardItem({ 'text/plain': blob })]);
    } else {
      await navigator.clipboard.writeText(await text);
    }
    return true;
  } catch {
    return false;
  }
}
