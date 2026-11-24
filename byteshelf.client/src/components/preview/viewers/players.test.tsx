// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AUDIO } from './audio';
import { VIDEO } from './video';
import { blobReader, type ContentReader } from '../../../lib/content/contentReader';
import type { Viewer } from '../../../lib/preview/viewerRegistry';
import type { ShelfFile } from '../../../services/shelfService';
import { press, untilFound } from '../../../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** MediaError's codes, which happy-dom does not have. */
const MEDIA_ERR_NETWORK = 2;
const MEDIA_ERR_DECODE = 3;
const MEDIA_ERR_SRC_NOT_SUPPORTED = 4;

describe.each<[string, Viewer, string, string]>([
  ['video', VIDEO, 'video/mp4', 'This browser cannot play this video.'],
  ['audio', AUDIO, 'audio/mpeg', 'This browser cannot play this recording.'],
])('the %s viewer', (tag, { component: Player }, type, cannotPlay) => {
  let root: Root;
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  async function play(content: ContentReader): Promise<HTMLMediaElement> {
    await act(async () => root.render(<Player file={{ name: 'a' } as ShelfFile} content={content} />));
    return found();
  }

  function found(): Promise<HTMLMediaElement> {
    return untilFound<HTMLMediaElement>(tag);
  }

  /** Fails the player with the browser's error `code`. */
  async function fail(player: HTMLMediaElement, code: number): Promise<void> {
    Object.defineProperty(player, 'error', { value: { code, MEDIA_ERR_NETWORK } });
    await act(async () => void player.dispatchEvent(new Event('error')));
  }

  it('plays the file in the browser\'s player, and says when the browser cannot', async () => {
    const player = await play(blobReader(new Blob(['a'], { type })));
    expect([player.controls, player.getAttribute('src')?.startsWith('blob:')]).toEqual([true, true]);

    await fail(player, MEDIA_ERR_DECODE);

    await vi.waitFor(() => expect(container.textContent).toBe(`${cannotPlay}Try again`));
  });

  it('plays on from a fresh address when the connection was lost and Try again is pressed', async () => {
    let addresses = 0;
    const player = await play({ ...blobReader(new Blob(['a'], { type })), url: async () => `/open/${++addresses}` });

    await fail(player, MEDIA_ERR_NETWORK);
    await vi.waitFor(() => expect(container.textContent).toBe('The connection was lost.Try again'));
    await press('Try again');

    expect((await found()).getAttribute('src')).toBe('/open/2');
  });

  it('says why the file could not be read, and plays it when Try again is pressed once it can be', async () => {
    const reader = blobReader(new Blob(['a'], { type }));
    const player = await play({ ...reader, range: () => Promise.reject(new Error('The server is unreachable.')) });

    await fail(player, MEDIA_ERR_SRC_NOT_SUPPORTED);
    await vi.waitFor(() => expect(container.textContent).toBe('The server is unreachable.Try again'));
    await press('Try again');

    await found();
  });
});
