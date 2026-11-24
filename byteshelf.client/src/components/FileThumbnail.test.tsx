// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FileThumbnail } from './FileThumbnail';
import { liveContent } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { stubServer } from '../test/fakeServer';
import { shelfFile } from '../test/shelfFiles';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const cookie = vi.hoisted(() => ({ asked: 0 }));

vi.mock('../services/authService', async (original) => ({
  ...(await original<typeof import('../services/authService')>()),
  hasFreshCookie: () => {
    cookie.asked += 1;
    return Promise.resolve(true);
  },
}));

/** Brings what is watched into view. */
let scrollTo: () => void;
let container: HTMLElement;
let root: Root;

beforeEach(() => {
  cookie.asked = 0;
  scrollTo = () => undefined;
  vi.stubGlobal('isSecureContext', true);
  vi.stubGlobal('IntersectionObserver', class {
    constructor(seen: (entries: { isIntersecting: boolean }[]) => void) {
      scrollTo = () => seen([{ isIntersecting: true }]);
    }
    observe() {}
    disconnect() {}
  });
  container = document.body.appendChild(document.createElement('div'));
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const SOURCE = liveContent('docs');

const PHOTO = shelfFile('photo.jpg', { sha256: 'abc', derived: { thumbnail: 'images-1' } });

async function show(file: ShelfFile, source = SOURCE): Promise<void> {
  await act(async () => root.render(<FileThumbnail file={file} source={source} />));
}

function image(): HTMLImageElement | null {
  return container.querySelector('img');
}

function icon(): Element | undefined {
  return Array.from(container.firstElementChild!.children).find((each) => each !== image());
}

describe('FileThumbnail', () => {
  it('loads the thumbnail once the row comes into view, with the icon until it is there', async () => {
    await show(PHOTO);
    expect(image()).toBeNull();
    expect(cookie.asked).toBe(0);

    await act(async () => scrollTo());
    expect(image()!.getAttribute('src')).toBe('/api/open/docs/files/derived?id=photo.jpg&kind=thumbnail&v=abc-images-1');
    expect(icon()).toBeDefined();

    await act(async () => image()!.dispatchEvent(new Event('load')));
    expect(icon()).toBeUndefined();
    expect(image()!.classList.contains('hidden')).toBe(false);
  });

  it('shows the icon and asks for nothing when there is no thumbnail', async () => {
    await show(shelfFile('notes.txt', { derived: { preview: 'images-1' } }));
    await act(async () => scrollTo());

    expect(icon()).toBeDefined();
    expect(image()).toBeNull();
    expect(cookie.asked).toBe(0);
  });

  it('asks for a thumbnail that does not load once more, and goes back to the icon when that does not either', async () => {
    const sent = stubServer(async () => new Response(null, { headers: { ETag: '"made anew"' } }));
    await show(PHOTO);
    await act(async () => scrollTo());
    await act(async () => image()!.dispatchEvent(new Event('error')));
    await vi.waitFor(() => expect(image()?.getAttribute('src')).toContain('made+anew'));
    expect(sent).toHaveLength(1);
    expect(icon()).toBeDefined();

    await act(async () => image()!.dispatchEvent(new Event('error')));
    expect(icon()).toBeDefined();
    expect(image()).toBeNull();
    expect(sent).toHaveLength(1);
  });

  it('loads another address once another is made, and none once the file is encrypted', async () => {
    await show(PHOTO);
    await act(async () => scrollTo());
    await show({ ...PHOTO, derived: { thumbnail: 'images-2' } });
    expect(image()!.getAttribute('src')).toContain('v=abc-images-2');

    await show({ ...PHOTO, sha256: 'def', derived: { thumbnail: 'images-2' } });
    expect(image()!.getAttribute('src')).toContain('v=def-images-2');

    await show({ ...PHOTO, sha256: 'def', derived: { thumbnail: 'images-2' } }, liveContent('other'));
    expect(image()!.getAttribute('src')).toContain('/api/open/other/');

    await show({ ...PHOTO, encrypted: true, sha256: 'other', derived: null });
    expect(image()).toBeNull();
  });

  it('fetches it with the header where the page keeps no cookie, and gives its blob up with the row', async () => {
    vi.stubGlobal('isSecureContext', false);
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('picture', { headers: { 'Content-Type': 'image/webp' } }))));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:thumbnail');
    const revoked = vi.spyOn(URL, 'revokeObjectURL').mockReturnValue();
    await show(PHOTO);
    await act(async () => scrollTo());
    await vi.waitFor(() => expect(image()?.getAttribute('src')).toBe('blob:thumbnail'));
    expect(cookie.asked).toBe(0);

    await show(shelfFile('notes.txt'));
    expect(revoked).toHaveBeenCalledWith('blob:thumbnail');
  });
});
