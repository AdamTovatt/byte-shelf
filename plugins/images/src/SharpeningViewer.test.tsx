import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentReader, DerivedKind, DerivedReader, Neighbour, ShelfFile } from '@byteshelf/plugin';
import { SharpeningViewer } from './SharpeningViewer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const RECORDED = { width: 4000, height: 3000, placeholder: 'data:image/webp;base64,AAAA' };

let documents: { data?: { contents: Record<string, unknown> | null }; isPending: boolean };
const documentsOf = vi.fn((file: ShelfFile) => (file.id === 'f' ? documents : { isPending: false }));
vi.mock('@byteshelf/plugin', async (original) => ({ ...(await original<object>()), usePluginDocuments: (file: ShelfFile) => documentsOf(file) }));

const shown = new Set(['image/png', 'image/jpeg']);
vi.mock('./showsType', () => ({ showsType: async (type: string) => shown.has(type) }));

describe('the sharpening viewer', () => {
  let root: Root;
  let container: HTMLDivElement;
  let loadedAhead: HTMLImageElement[];
  let contentUrl: ReturnType<typeof vi.fn<ContentReader['url']>>;
  let decode: () => Promise<void>;

  /** What was made of an image: each kind listed is there, and each in `made` has or comes to have a URL, null for none. */
  function derivedReader(listed: DerivedKind[], made: Partial<Record<DerivedKind, Promise<string | null>>> = {}) {
    const url = vi.fn((kind: DerivedKind) => made[kind] ?? Promise.resolve(listed.includes(kind) ? `${kind}.webp` : null));
    return { has: (kind: DerivedKind) => listed.includes(kind), url, forget: vi.fn() } satisfies DerivedReader;
  }

  async function show(derived: DerivedReader, file: Partial<ShelfFile> = {}, neighbours: Neighbour[] = []): Promise<void> {
    const content: ContentReader = { url: contentUrl, blob: async () => new Blob(), range: async () => new Blob(['a']) };
    const shows = { id: 'f', name: 'a.png', contentType: 'image/png', encrypted: false, ...file } as ShelfFile;
    await act(async () => root.render(<SharpeningViewer file={shows} content={content} derived={derived} neighbours={neighbours} />));
  }

  /** The images there are, by what they load. */
  function images(): string[] {
    return [...new Set([...container.querySelectorAll('img')].map((each) => each.getAttribute('src')!))];
  }

  function imageOf(src: string): HTMLImageElement {
    return container.querySelector<HTMLImageElement>(`img[src="${src}"]`)!;
  }

  /** What is seen, which has the file's name; null when that is the placeholder or nothing. */
  function seen(): string | null {
    const [image, ...more] = container.querySelectorAll('img:not([alt=""])');
    expect(more).toEqual([]);
    return image?.getAttribute('src') ?? null;
  }

  async function load(src: string, width = 400, height = 300): Promise<void> {
    const image = imageOf(src);
    Object.defineProperties(image, { naturalWidth: { value: width }, naturalHeight: { value: height } });
    await act(async () => void image.dispatchEvent(new Event('load')));
  }

  async function fail(src: string): Promise<void> {
    await act(async () => void imageOf(src).dispatchEvent(new Event('error')));
  }

  function box(): HTMLElement {
    return container.querySelector<HTMLElement>('.images-box')!;
  }

  function boxSize(): (string | undefined)[] {
    return ['--images-width', '--images-height'].map((name) => box()?.style.getPropertyValue(name));
  }

  beforeEach(() => {
    documents = { data: { contents: RECORDED }, isPending: false };
    documentsOf.mockClear();
    loadedAhead = [];
    contentUrl = vi.fn(async () => 'whole.png');
    decode = async () => undefined;
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockImplementation(() => decode());
    const ImageElement = Image;
    vi.stubGlobal('Image', class extends ImageElement {
      constructor() {
        super();
        loadedAhead.push(this);
      }
    });
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('has the image\'s box and its placeholder before anything is loaded', async () => {
    await show(derivedReader(['thumbnail', 'preview'], { preview: new Promise(() => {}), thumbnail: new Promise(() => {}) }));

    expect(boxSize()).toEqual(['4000', '3000']);
    expect(images()).toContain(RECORDED.placeholder);
    expect(seen()).toBeNull();
  });

  it('shows the thumbnail, the preview and then the whole, each in place of what was there, in a box that stays', async () => {
    await show(derivedReader(['thumbnail', 'preview']));
    const first = box();
    expect(images()).toEqual([RECORDED.placeholder, 'thumbnail.webp', 'preview.webp']);

    await load('thumbnail.webp', 320, 240);
    expect([seen(), images()]).toEqual(['thumbnail.webp', ['thumbnail.webp', 'preview.webp']]);
    await load('preview.webp', 2048, 1536);
    expect([seen(), images()]).toEqual(['preview.webp', ['preview.webp', 'whole.png']]);
    await load('whole.png', 4000, 3000);
    expect([seen(), images()]).toEqual(['whole.png', ['whole.png']]);

    expect(box()).toBe(first);
    expect(boxSize()).toEqual(['4000', '3000']);
  });

  it('asks for nothing made for showing the whole of a type the browser shows', async () => {
    const derived = derivedReader(['preview', 'full']);
    await show(derived);
    await load('preview.webp');

    expect(images()).toEqual(['preview.webp', 'whole.png']);
    expect(derived.url.mock.calls.map(([kind]) => kind)).not.toContain('full');
  });

  it('keeps the preview when the thumbnail is decoded just after it', async () => {
    const decoded: (() => void)[] = [];
    decode = () => new Promise((resolve) => decoded.push(resolve));
    await show(derivedReader(['thumbnail', 'preview']));
    await load('thumbnail.webp');
    await load('preview.webp');

    const [thumbnailDecoded, previewDecoded] = decoded;
    await act(async () => {
      previewDecoded();
      thumbnailDecoded();
    });

    expect(seen()).toBe('preview.webp');
  });

  it('keeps its size and what is shown of what is no longer listed or recorded', async () => {
    const derived = derivedReader(['thumbnail', 'preview']);
    await show(derived);
    await load('thumbnail.webp');

    derived.has = () => false;
    documents = { data: { contents: null }, isPending: false };
    await show(derived);

    expect([seen(), boxSize()]).toEqual(['thumbnail.webp', ['4000', '3000']]);
  });

  it('loads the preview beside the thumbnail, and never steps back to it', async () => {
    await show(derivedReader(['thumbnail', 'preview']));
    const thumbnail = imageOf('thumbnail.webp');

    await load('thumbnail.webp');
    await load('preview.webp');

    expect([seen(), images()]).toEqual(['preview.webp', ['preview.webp', 'whole.png']]);
    expect(thumbnail.hasAttribute('src')).toBe(false);
  });

  it('keeps what is there, and the next unseen, until the next has been decoded', async () => {
    let decoded = () => {};
    await show(derivedReader(['preview']));
    await load('preview.webp');
    decode = () => new Promise((resolve) => (decoded = resolve));

    await load('whole.png');
    expect([seen(), images()]).toEqual(['preview.webp', ['preview.webp', 'whole.png']]);
    expect(imageOf('whole.png').className).toContain('images-opacity-0');
    await act(async () => decoded());

    expect([seen(), images()]).toEqual(['whole.png', ['whole.png']]);
    expect(imageOf('whole.png').className).not.toContain('opacity');
  });

  it('keeps the zoom as the whole takes the preview\'s place', async () => {
    await show(derivedReader(['preview']));
    await load('preview.webp');
    // Laid out in a 400 by 300 frame, which the test page does not do itself.
    Object.defineProperties(box(), { offsetWidth: { value: 400 }, offsetHeight: { value: 300 } });
    Object.defineProperties(container.querySelector('.images-stage')!.parentElement!, { clientWidth: { value: 400 }, clientHeight: { value: 300 } });
    const fitted = box().style.transform;
    act(() => void container.querySelector('.images-stage')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true })));
    const zoom = box().style.transform;
    expect(zoom).not.toBe(fitted);

    await load('whole.png');

    expect([seen(), box().style.transform]).toEqual(['whole.png', zoom]);
  });

  it('goes on without a thumbnail that does not load', async () => {
    const derived = derivedReader(['thumbnail', 'preview']);
    await show(derived);

    await fail('thumbnail.webp');
    await fail('thumbnail.webp');
    expect(images()).toEqual([RECORDED.placeholder, 'preview.webp']);
    await load('preview.webp');
    await load('whole.png');

    expect([seen(), images()]).toEqual(['whole.png', ['whole.png']]);
  });

  it('keeps the preview when the whole does not load', async () => {
    await show(derivedReader(['preview']));
    await load('preview.webp');

    await fail('whole.png');

    expect([seen(), images()]).toEqual(['preview.webp', ['preview.webp']]);
    expect(contentUrl).toHaveBeenCalledTimes(1);
    expect(container.textContent).toBe('');
  });

  it('asks anew once for what was listed and does not load, and then goes on without it', async () => {
    const derived = derivedReader(['preview']);
    await show(derived);

    await fail('preview.webp');
    expect(derived.forget.mock.calls).toEqual([['preview']]);
    expect(images()).toContain('preview.webp');
    await fail('preview.webp');

    expect(derived.forget).toHaveBeenCalledTimes(1);
    expect(images()).toEqual([RECORDED.placeholder, 'whole.png']);
  });

  it('loads the whole from what was made for it of a type the browser does not show', async () => {
    const derived = derivedReader(['preview'], { full: Promise.resolve('full.jpg') });
    await show(derived, { contentType: 'image/heic' });
    await load('preview.webp');

    await load('full.jpg');

    expect(seen()).toBe('full.jpg');
    expect(contentUrl).not.toHaveBeenCalled();
  });

  it('loads the contents of a type the browser does not show when nothing was made for that', async () => {
    await show(derivedReader(['preview']), { contentType: 'image/heic' });
    await load('preview.webp');

    expect(images()).toEqual(['preview.webp', 'whole.png']);
  });

  it('loads the whole at once when there was no preview yet, which is shown when it comes first', async () => {
    let made: (url: string) => void = () => {};
    await show(derivedReader([], { preview: new Promise((resolve) => (made = resolve)) }));
    expect(images()).toContain('whole.png');

    await act(async () => made('preview.webp'));
    await load('preview.webp');
    expect(seen()).toBe('preview.webp');
    await load('whole.png');

    expect(seen()).toBe('whole.png');
  });

  it('ends what is loading as it goes', async () => {
    const derived = derivedReader(['preview']);
    await show(derived);
    await load('preview.webp');
    const whole = imageOf('whole.png');

    await act(async () => root.render(null));

    expect(whole.hasAttribute('src')).toBe(false);
    expect(contentUrl.mock.calls[0][0]!.aborted).toBe(true);
  });

  describe('of which no size is recorded', () => {
    beforeEach(() => {
      documents = { data: { contents: null }, isPending: false };
    });

    it('has the preview\'s size, which it keeps, and no thumbnail', async () => {
      await show(derivedReader(['thumbnail', 'preview']));
      expect(images()).toEqual(['preview.webp']);
      expect(boxSize()).toEqual([undefined, undefined]);

      await load('preview.webp', 2048, 1365);
      await load('whole.png', 4000, 2667);

      expect([seen(), boxSize()]).toEqual(['whole.png', ['2048', '1365']]);
    });

    it('waits for what is recorded before it leaves it to the app\'s viewer, and keeps it when something is', async () => {
      documents = { isPending: true };
      const derived = derivedReader([]);
      await show(derived);
      expect(container.querySelector('.images-stage')).not.toBeNull();

      documents = { data: { contents: RECORDED }, isPending: false };
      await show(derived);

      expect(boxSize()).toEqual(['4000', '3000']);
      expect(images()).toEqual([RECORDED.placeholder, 'whole.png']);
    });

    it('has the size of the whole when there is no preview', async () => {
      await show(derivedReader([], { preview: new Promise(() => {}) }));

      await load('whole.png', 4000, 2667);

      expect([seen(), boxSize()]).toEqual(['whole.png', ['4000', '2667']]);
    });

    it('keeps the size it has when one is recorded later', async () => {
      const derived = derivedReader(['preview']);
      documents = { isPending: true };
      await show(derived);
      await load('preview.webp', 2048, 1536);

      documents = { data: { contents: RECORDED }, isPending: false };
      await show(derived);

      expect(boxSize()).toEqual(['2048', '1536']);
    });
  });

  describe('leaves it to the app\'s viewer', () => {
    const tryAgain = () => container.querySelector('button')?.textContent;

    it('when nothing is made of the image', async () => {
      documents = { data: { contents: null }, isPending: false };
      await show(derivedReader([]));

      expect([seen(), box()]).toEqual(['whole.png', null]);
    });

    it.each([{ encrypted: true }, { contentType: 'image/svg+xml; charset=utf-8' }])('when it is %o', async (file) => {
      const derived = derivedReader([]);
      await show(derived, file);

      expect([seen(), box()]).toEqual(['whole.png', null]);
      expect(derived.url).not.toHaveBeenCalled();
      expect(documentsOf).not.toHaveBeenCalled();
    });

    it('to say why there is nothing to show, and to try again', async () => {
      await show(derivedReader([]));
      expect(images()).toEqual([RECORDED.placeholder, 'whole.png']);

      await fail('whole.png');
      await fail('whole.png');

      await vi.waitFor(() => expect(container.textContent).toContain('This browser cannot show this image.'));
      expect(tryAgain()).toBe('Try again');
    });

    it('to say why when there is only a thumbnail to show', async () => {
      await show(derivedReader(['thumbnail']));
      await load('thumbnail.webp');
      expect(seen()).toBe('thumbnail.webp');

      await fail('whole.png');
      await fail('whole.png');

      await vi.waitFor(() => expect(container.textContent).toContain('This browser cannot show this image.'));
    });
  });

  describe('loads ahead', () => {
    const neighbour = (id: string, contentType: string, listed: DerivedKind[]): Neighbour & { derived: ReturnType<typeof derivedReader> } => ({
      file: { id, contentType } as ShelfFile,
      derived: derivedReader(listed, { preview: Promise.resolve(`${id}.webp`) }),
    });

    it('the previews of the images beside it, and their documents, and nothing else', async () => {
      const neighbours = [neighbour('before', 'image/jpeg', ['thumbnail', 'preview']), neighbour('after', 'image/png', ['thumbnail'])];
      await show(derivedReader(['preview']), {}, neighbours);

      expect(loadedAhead.map((each) => each.getAttribute('src'))).toEqual(['before.webp']);
      expect(neighbours.map(({ derived }) => derived.url.mock.calls.map(([kind]) => kind))).toEqual([['preview'], []]);
      expect(documentsOf.mock.calls.map(([{ id }]) => id)).toEqual(expect.arrayContaining(['before', 'after']));
    });

    it('nothing of files beside it that it does not show', async () => {
      const video = neighbour('video', 'video/mp4', ['preview']);
      await show(derivedReader(['preview']), {}, [video]);

      expect(video.derived.url).not.toHaveBeenCalled();
      expect(documentsOf.mock.calls.map(([{ id }]) => id)).not.toContain('video');
    });

    it('until it goes, and what is shown next has begun', async () => {
      await show(derivedReader(['preview']), {}, [neighbour('after', 'image/png', ['preview'])]);
      const [ahead] = loadedAhead;

      vi.useFakeTimers();
      await act(async () => root.render(null));
      expect(ahead.hasAttribute('src')).toBe(true);
      vi.runAllTimers();

      expect(ahead.hasAttribute('src')).toBe(false);
    });
  });
});
