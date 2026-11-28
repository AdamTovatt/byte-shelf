import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentReader, DerivedKind, DerivedReader, DerivedStatus, ShelfFile } from '@byteshelf/plugin';
import { InPreview, derivedReaderOf } from '@byteshelf/plugin/testing';
import { savePosition, savedPosition } from './resumePoint';
import { VideoPlayer } from './VideoPlayer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const STORYBOARD = { intervalSeconds: 2, columns: 10, rows: 1, frameWidth: 160, frameHeight: 90, count: 10 };

let documents: { data?: { contents: Record<string, unknown> | null } };
vi.mock('@byteshelf/plugin', async (original) => ({ ...(await original<object>()), usePluginDocuments: () => documents }));

/** The viewer after this one, as the app gives it. */
function NextViewer() {
  return <p>the next viewer</p>;
}

/** Where each kind made of a video is: the stream at `stream.mp4`, any other at `<kind>.webp`. */
const MADE_AT: Record<DerivedKind, string> = { thumbnail: 'thumbnail.webp', preview: 'preview.webp', full: 'full.webp', agent: 'agent.webp', stream: 'stream.mp4' };

describe('the video player', () => {
  let root: Root;
  let container: HTMLDivElement;

  /** What was made of a video, each kind listed at its place, and what it says of a stream not listed. */
  function derivedReader(listed: DerivedKind[], streamStatus: DerivedStatus | Error = 'none') {
    const reader = derivedReaderOf(Object.fromEntries(listed.map((kind) => [kind, MADE_AT[kind]])));
    const status = vi.fn(async () => {
      if (streamStatus instanceof Error) throw streamStatus;
      return streamStatus;
    });
    return { reader: { ...reader, status } satisfies DerivedReader, status };
  }

  async function show(derived: DerivedReader, file: Partial<ShelfFile> = {}): Promise<void> {
    const content: ContentReader = { url: async () => 'original.mp4', blob: async () => new Blob(), range: async () => new Blob(['a']) };
    const shows = { id: 'f', name: 'a.mp4', contentType: 'video/mp4', encrypted: false, ...file } as ShelfFile;
    await act(async () => root.render(<InPreview><VideoPlayer file={shows} content={content} derived={derived} neighbours={[]} Fallback={NextViewer} /></InPreview>));
  }

  function video(): HTMLVideoElement | null {
    return container.querySelector('video');
  }

  function control(label: string): HTMLElement | null {
    return container.querySelector(`[aria-label="${label}"]`);
  }

  async function click(label: string): Promise<void> {
    await act(async () => control(label)!.click());
  }

  async function dispatch(element: EventTarget, type: string): Promise<void> {
    await act(async () => void element.dispatchEvent(new Event(type)));
  }

  /** The video as it loads what it plays, of 100 seconds, which then plays and pauses as asked. */
  async function loaded(): Promise<HTMLVideoElement> {
    const element = video()!;
    Object.defineProperties(element, { duration: { value: 100, configurable: true }, readyState: { value: 1, configurable: true } });
    let isPaused = true;
    Object.defineProperty(element, 'paused', { get: () => isPaused, configurable: true });
    element.play = vi.fn(async () => {
      isPaused = false;
      element.dispatchEvent(new Event('play'));
    });
    element.pause = vi.fn(() => {
      isPaused = true;
      element.dispatchEvent(new Event('pause'));
    });
    await dispatch(element, 'loadedmetadata');
    await dispatch(element, 'durationchange');
    return element;
  }

  /** The video fails as one that cannot be played does, or, given its code, as `MediaError` says. */
  async function fail(code?: number): Promise<void> {
    const element = video()!;
    if (code !== undefined) Object.defineProperty(element, 'error', { value: { code }, configurable: true });
    await dispatch(element, 'error');
  }

  async function press(key: string, target: EventTarget = document.body): Promise<void> {
    await act(async () => void target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })));
  }

  beforeEach(() => {
    localStorage.clear();
    documents = { data: { contents: { durationSeconds: 100, width: 1920, height: 1080, streamsWell: false, storyboard: STORYBOARD } } };
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.restoreAllMocks();
  });

  it('plays the stream when the listing names one, with the poster, and asks nothing of it', async () => {
    const { reader, status } = derivedReader(['thumbnail', 'preview', 'stream']);
    await show(reader);

    expect([video()?.getAttribute('src'), video()?.getAttribute('poster')]).toEqual(['stream.mp4', 'thumbnail.webp']);
    expect(status).not.toHaveBeenCalled();
  });

  it('plays the original of a video that streams well, and never asks for its stream', async () => {
    documents = { data: { contents: { ...documents.data!.contents, streamsWell: true } } };
    const { reader, status } = derivedReader(['thumbnail']);
    await show(reader);

    expect(video()?.getAttribute('src')).toBe('original.mp4');
    expect(status).not.toHaveBeenCalled();
  });

  it('plays the original while its stream is on its way or there is no room for it, and says so, and says nothing when asking fails', async () => {
    const notes = [];
    for (const status of ['making', 'noRoom', new Error('Refused.')] as const) {
      const { reader, status: asked } = derivedReader([], status);
      await show(reader, { id: String(notes.length) });
      expect([video()?.getAttribute('src'), asked]).toEqual(['original.mp4', expect.objectContaining({ mock: expect.objectContaining({ calls: [['stream', expect.any(AbortSignal)]] }) })]);
      notes.push(container.querySelector('p')?.textContent ?? null);
      await act(async () => root.render(null));
    }

    expect(notes).toEqual(['A streaming copy is on its way.', 'There is no room on this shelf for a streaming copy.', null]);
  });

  it('plays the original of a stream listed that is gone since', async () => {
    const { reader } = derivedReader(['stream']);
    await show({ ...reader, url: async (kind) => (kind === 'stream' ? null : reader.url(kind)) });

    expect(video()?.getAttribute('src')).toBe('original.mp4');
  });

  it('plays the original where a stream that does not play left off, and leaves one it cannot play either to the next viewer', async () => {
    await show(derivedReader(['stream']).reader);
    const stream = await loaded();
    stream.currentTime = 42;

    await fail();
    expect(video()?.getAttribute('src')).toBe('original.mp4');
    expect((await loaded()).currentTime).toBe(42);

    await fail();
    expect([video(), container.textContent]).toEqual([null, 'the next viewer']);
  });

  it('picks the original up where the video was stopped when the stream fails before it loaded anything', async () => {
    savePosition('f', 30);
    await show(derivedReader(['stream']).reader);

    await fail();

    expect([video()?.getAttribute('src'), (await loaded()).currentTime]).toEqual(['original.mp4', 30]);
  });

  it('keeps what it plays when the network cuts it off, and loads it again on play, playing on from where it was', async () => {
    await show(derivedReader(['stream']).reader);
    const cutOff = await loaded();
    cutOff.currentTime = 42;

    await fail(2);
    expect([video(), container.textContent]).toEqual([cutOff, expect.stringContaining('The video stopped loading.')]);

    await click('Play');
    const again = await loaded();
    expect([again === cutOff, again.getAttribute('src'), again.currentTime, again.play]).toEqual([false, 'stream.mp4', 42, expect.objectContaining({ mock: expect.objectContaining({ calls: [[]] }) })]);
  });

  it('plays the original instead of the stream and back, each from where the other was, at its speed, playing as it was', async () => {
    await show(derivedReader(['stream']).reader);
    const stream = await loaded();
    stream.currentTime = 42;
    stream.playbackRate = 1.5;
    await click('Play');

    await click('Original');
    const original = await loaded();
    expect([original.getAttribute('src'), original.currentTime, original.playbackRate, original.paused]).toEqual(['original.mp4', 42, 1.5, false]);
    expect(control('Original')?.getAttribute('aria-pressed')).toBe('true');

    original.currentTime = 50;
    await click('Original');
    const back = await loaded();
    expect([back.getAttribute('src'), back.currentTime]).toEqual(['stream.mp4', 50]);
    expect(control('Original')?.getAttribute('aria-pressed')).toBe('false');
  });

  it('plays the stream again when the original chosen over it does not play, with no original to choose, and has none to choose of a video with no stream', async () => {
    await show(derivedReader(['stream']).reader);
    (await loaded()).currentTime = 42;
    await click('Original');
    (await loaded()).currentTime = 50;

    await fail();

    expect([video()?.getAttribute('src'), (await loaded()).currentTime, control('Original')]).toEqual(['stream.mp4', 50, null]);
    await act(async () => root.render(null));
    await show(derivedReader([]).reader);
    expect(control('Original')).toBeNull();
  });

  it('goes on playing the original when the stream is made as it plays', async () => {
    const { reader } = derivedReader([]);
    let isMade = false;
    const making: DerivedReader = {
      ...reader,
      has: (kind) => (kind === 'stream' ? isMade : reader.has(kind)),
      url: async (kind) => (kind === 'stream' && isMade ? MADE_AT.stream : reader.url(kind)),
    };
    await show(making);

    isMade = true;
    await show(making);

    expect(video()?.getAttribute('src')).toBe('original.mp4');
  });

  it('leaves an encrypted video to the next viewer', async () => {
    await show(derivedReader(['stream']).reader, { encrypted: true });

    expect([video(), container.textContent]).toEqual([null, 'the next viewer']);
  });

  it('picks up where it was stopped, and keeps where it is as it is paused, every 2 seconds as it plays and as it closes, and forgets it once ended', async () => {
    savePosition('f', 30);
    await show(derivedReader([]).reader);
    const element = await loaded();
    expect(element.currentTime).toBe(30);

    const now = vi.spyOn(Date, 'now').mockReturnValue(10_000);
    element.currentTime = 40;
    await dispatch(element, 'timeupdate');
    element.currentTime = 41;
    now.mockReturnValue(11_000);
    await dispatch(element, 'timeupdate');
    expect(savedPosition('f')).toBe(40);
    now.mockReturnValue(12_500);
    await dispatch(element, 'timeupdate');
    expect(savedPosition('f')).toBe(41);

    element.currentTime = 55;
    await dispatch(element, 'pause');
    expect(savedPosition('f')).toBe(55);

    element.currentTime = 60;
    await act(async () => root.render(null));
    expect(savedPosition('f')).toBe(60);

    await show(derivedReader([]).reader);
    const watched = await loaded();
    Object.defineProperty(watched, 'ended', { value: true, configurable: true });
    await dispatch(watched, 'ended');
    expect(savedPosition('f')).toBeNull();
  });

  it('plays and pauses with K and Space, and jumps with J and L, of the keys its preview hands it, those on the volume slider too', async () => {
    await show(derivedReader([]).reader);
    const element = await loaded();
    element.currentTime = 50;

    await press('k');
    expect(element.paused).toBe(false);
    await press(' ', container);
    expect(element.paused).toBe(true);
    await press('l');
    await press('j');
    await press('j');
    expect(element.currentTime).toBe(40);

    await press('k', control('Volume')!);
    expect(element.paused).toBe(false);
  });

  it('moves on from where a seek went with the seek bar\'s keys before the video says it is there', async () => {
    await show(derivedReader([]).reader);
    const element = await loaded();
    const seekBar = control('Seek')!;

    await press('ArrowRight', seekBar);
    await press('ArrowRight', seekBar);

    expect([element.currentTime, seekBar.getAttribute('aria-valuenow')]).toEqual([10, '10']);
  });

  it('goes through its speeds, from any other to the first', async () => {
    await show(derivedReader([]).reader);
    const element = await loaded();
    const rates = [];
    for (let click = 0; click < 6; click++) {
      await act(async () => control('Speed 1×')!.click());
      rates.push(element.playbackRate);
    }
    element.playbackRate = 3;
    await act(async () => control('Speed 1×')!.click());

    expect([...rates, element.playbackRate]).toEqual([1.25, 1.5, 2, 0.5, 0.75, 1, 1]);
  });

  it('mutes and unmutes, at the whole volume when it was at none, and sets the volume, muted at none', async () => {
    await show(derivedReader([]).reader);
    const element = await loaded();

    await click('Mute');
    expect(element.muted).toBe(true);
    await dispatch(element, 'volumechange');
    await click('Unmute');
    expect(element.muted).toBe(false);

    const volume = control('Volume') as HTMLInputElement;
    const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!;
    const levels = [];
    for (const level of ['0.5', '0']) {
      await act(async () => {
        setValue.call(volume, level);
        volume.dispatchEvent(new Event('input', { bubbles: true }));
      });
      levels.push([element.volume, element.muted]);
      await dispatch(element, 'volumechange');
    }
    expect(levels).toEqual([[0.5, false], [0, true]]);

    await click('Unmute');
    expect([element.volume, element.muted]).toEqual([1, false]);
  });

  it('goes full screen with the page and back, and stays as it is when the browser refuses', async () => {
    // What a browser that makes elements full screen has, which the test's has not; each refusal says whether it was handled.
    const handled: string[] = [];
    function refusal(of: string): Promise<void> {
      const refused = Promise.reject(new Error('Refused.'));
      refused.catch = (onRejected) => {
        handled.push(of);
        return Promise.prototype.catch.call(refused, onRejected);
      };
      return refused;
    }
    let shownFullScreen: Element | null = null;
    Object.defineProperties(document, {
      fullscreenEnabled: { value: true, configurable: true },
      fullscreenElement: { get: () => shownFullScreen, configurable: true },
      exitFullscreen: { value: () => refusal('exit'), configurable: true },
    });
    try {
      await show(derivedReader([]).reader);
      document.documentElement.requestFullscreen = () => refusal('request');

      await click('Full screen');
      shownFullScreen = document.documentElement;
      await dispatch(document, 'fullscreenchange');
      await click('Exit full screen');

      expect(handled).toEqual(['request', 'exit']);
    } finally {
      for (const name of ['fullscreenEnabled', 'fullscreenElement', 'exitFullscreen']) Reflect.deleteProperty(document, name);
      Reflect.deleteProperty(document.documentElement, 'requestFullscreen');
    }
  });

  it('goes full screen in the browser\'s own player where the browser makes no element full screen, as on an iPhone', async () => {
    const enter = vi.fn();
    Object.defineProperty(HTMLVideoElement.prototype, 'webkitEnterFullscreen', { value: enter, configurable: true });
    try {
      await show(derivedReader([]).reader);

      await click('Full screen');

      expect(enter.mock.contexts).toEqual([video()]);
    } finally {
      Reflect.deleteProperty(HTMLVideoElement.prototype, 'webkitEnterFullscreen');
    }
  });
});
