// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TextViewer } from './TextViewer';
import { blobReader } from '../../../lib/preview/contentReader';
import type { ShelfFile } from '../../../services/shelfService';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const MIB = 1024 * 1024;

describe('the text viewer', () => {
  let root: Root;
  let container: HTMLDivElement;

  async function show(contents: string | Uint8Array<ArrayBuffer>): Promise<string> {
    const content = blobReader(new Blob([contents]));
    await act(async () => root.render(<TextViewer file={{} as ShelfFile} content={content} />));
    return vi.waitFor(() => {
      const shown = container.querySelector('pre');
      expect(shown).not.toBeNull();
      return shown!.textContent!;
    });
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
    expect(await show(new Uint8Array(bytes))).toBe('å');
  });

  it('says why the text could not be read', async () => {
    const content = { ...blobReader(new Blob([])), range: async () => { throw new Error('The server is unreachable.'); } };

    await act(async () => root.render(<TextViewer file={{} as ShelfFile} content={content} />));

    await vi.waitFor(() => expect(container.textContent).toContain('The server is unreachable.'));
  });

  it('shows an empty file as empty', async () => {
    expect(await show('')).toBe('');
  });

  it('shows markup as text', async () => {
    expect(await show('<script>alert(1)</script>')).toBe('<script>alert(1)</script>');
    expect(container.querySelector('script')).toBeNull();
  });
});
