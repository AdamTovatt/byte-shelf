// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TextViewer } from './TextViewer';
import { blobReader } from '../../../lib/preview/contentReader';
import { press, untilFound } from '../../../test/dom';
import { shelfFile } from '../../../test/shelfFiles';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const MIB = 1024 * 1024;

describe('the text viewer', () => {
  let root: Root;
  let container: HTMLDivElement;

  async function show(contents: string | Uint8Array<ArrayBuffer>, contentType = 'text/plain'): Promise<string> {
    const content = blobReader(new Blob([contents]));
    await act(async () => root.render(<TextViewer file={shelfFile('a', { contentType })} content={content} />));
    return (await untilFound('pre')).textContent!;
  }

  beforeEach(() => {
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('shows a file of 1 MiB whole', async () => {
    expect((await show('a'.repeat(MIB))).length).toBe(MIB);
    expect(container.textContent).not.toContain('Only the first');
  });

  it('shows the first 1 MiB of a larger one, saying so, and no half of a character the cut went through', async () => {
    // Two bytes each, starting at an odd byte, so the cut goes through one.
    const shown = await show(`a${'å'.repeat(MIB)}`);

    expect(container.textContent).toContain('Only the first 1 MiB is shown.');
    expect(shown).toBe(`a${'å'.repeat(MIB / 2 - 1)}`);
  });

  it.each([
    ['UTF-16 little-endian', [0xff, 0xfe, 0xe5, 0x00]],
    ['UTF-16 big-endian', [0xfe, 0xff, 0x00, 0xe5]],
    ['UTF-8', [0xef, 0xbb, 0xbf, 0xc3, 0xa5]],
  ])('shows text in %s by its byte order mark', async (_encoding, bytes) => {
    expect(await show(new Uint8Array(bytes), 'text/plain; charset=iso-8859-1')).toBe('å');
  });

  it.each([
    ['in the charset its type names', 'text/plain; charset=iso-8859-1', [0xe5]],
    ['in UTF-8 when its type names none', 'text/plain', [0xc3, 0xa5]],
    ['in UTF-8 when its type names one the browser does not know', 'text/plain; charset=none', [0xc3, 0xa5]],
  ])('shows text without a mark %s', async (_how, contentType, bytes) => {
    expect(await show(new Uint8Array(bytes), contentType)).toBe('å');
  });

  it('reads no more than it shows, and one byte to tell whether there is more', async () => {
    const range = vi.fn(async () => new Blob(['a']));

    await act(async () => root.render(<TextViewer file={shelfFile('a')} content={{ ...blobReader(new Blob([])), range }} />));

    await untilFound('pre');
    expect(range.mock.calls).toEqual([[0, MIB + 1, expect.any(AbortSignal)]]);
  });

  it('says why the text could not be read, and shows it when Try again is pressed once it can be', async () => {
    const range = vi.fn(async () => new Blob(['the text'])).mockRejectedValueOnce(new Error('The server is unreachable.'));

    await act(async () => root.render(<TextViewer file={shelfFile('a')} content={{ ...blobReader(new Blob([])), range }} />));
    await vi.waitFor(() => expect(container.textContent).toBe('The server is unreachable.Try again'));
    await press('Try again');

    expect((await untilFound('pre')).textContent).toBe('the text');
  });

  it('shows an empty file as empty', async () => {
    expect(await show('')).toBe('');
  });

  it('shows markup as text', async () => {
    expect(await show('<script>alert(1)</script>')).toBe('<script>alert(1)</script>');
    expect(container.querySelector('script')).toBeNull();
  });
});
