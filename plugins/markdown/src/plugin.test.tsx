import { act, Suspense } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentReader, ShelfFile, Viewer } from '@byteshelf/plugin';
import plugin from './plugin';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const LOAD_TIMEOUT = 20_000;

describe('the plugin', { timeout: 2 * LOAD_TIMEOUT }, () => {
  const [{ component: MarkdownViewer, mediaTypes, extensions }] = plugin.viewers as readonly Viewer[];
  let root: Root;
  let signal: AbortSignal | undefined;
  let contents: Blob;

  const content: ContentReader = {
    url: () => Promise.reject(new Error('It needs no address.')),
    blob: () => Promise.reject(new Error('It reads no more than it shows.')),
    range: (start, end, until) => {
      signal = until;
      return Promise.resolve(contents.slice(start, end));
    },
  };

  async function shows(text: string): Promise<void> {
    await act(async () => root.render(<Suspense><MarkdownViewer file={{ contentType: 'text/markdown' } as ShelfFile} content={content} /></Suspense>));
    // Its component is loaded as it is first shown, which takes a while on a busy machine.
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.body.textContent).toContain(text);
    }, LOAD_TIMEOUT);
  }

  beforeEach(() => {
    signal = undefined;
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('shows Markdown, and untyped files named as it', () => {
    expect([plugin.viewers!.length, mediaTypes, extensions]).toEqual([1, ['text/markdown'], ['.md']]);
  });

  it('shows what a file holds, read until it is gone', async () => {
    contents = new Blob(['# Title']);

    await shows('Title');

    expect(document.querySelector('h1')!.textContent).toBe('Title');
    expect(signal!.aborted).toBe(false);
    act(() => root.render(null));
    expect(signal!.aborted).toBe(true);
  });

  it('says that only the start is shown of a file larger than is read', async () => {
    contents = new Blob(['a\n\n', new Uint8Array(1024 * 1024)]);

    await shows('a');
    act(() => document.querySelector<HTMLButtonElement>('button[aria-label="Source"]')!.click());

    expect(document.body.textContent).toContain('Only the first 1 MiB is shown.');
  });
});
