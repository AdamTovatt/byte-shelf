// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FileThumbnail } from './FileThumbnail';
import { liveContent, type ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { stubServer } from '../test/fakeServer';
import { stubIntersectionObserver, type Watching } from '../test/intersectionObserver';
import { shelfFile } from '../test/shelfFiles';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../services/authService', async (original) => ({
  ...(await original<typeof import('../services/authService')>()),
  hasFreshCookie: () => Promise.resolve(true),
}));

let watching: Watching[];
let container: HTMLElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal('isSecureContext', true);
  watching = stubIntersectionObserver();
  // A browser decodes what loaded; happy-dom has nothing to decode.
  vi.spyOn(HTMLImageElement.prototype, 'decode').mockResolvedValue();
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

/** Brings the row into view. */
async function scrollTo(): Promise<void> {
  await act(async () => watching.forEach((each) => each.see(true)));
}

function image(): HTMLImageElement | null {
  return container.querySelector('img');
}

/** The type's icon, which is the slot's child that is not the thumbnail. */
function icon(): Element | null {
  return Array.from(container.firstElementChild!.children).find((each) => each !== image()) ?? null;
}

async function loaded(): Promise<void> {
  await act(async () => image()!.dispatchEvent(new Event('load')));
}

async function failed(): Promise<void> {
  await act(async () => image()!.dispatchEvent(new Event('error')));
}

describe('FileThumbnail', () => {
  it('loads nothing until the row comes into view', async () => {
    await show(PHOTO);

    expect(image()).toBeNull();
    expect(icon()).toBeInstanceOf(Element);
  });

  it('loads the thumbnail once the row comes into view, with the icon until it is there', async () => {
    await show(PHOTO);
    await scrollTo();
    expect(image()!.getAttribute('src')).toBe('/api/open/docs/files/derived?id=photo.jpg&kind=thumbnail&v=abc-images-1');
    expect(image()!.classList.contains('hidden')).toBe(true);
    expect(icon()).toBeInstanceOf(Element);

    await loaded();
    expect(icon()).toBeNull();
    expect(image()!.classList.contains('hidden')).toBe(false);
  });

  it('shows the icon and loads nothing when there is no thumbnail', async () => {
    await show(shelfFile('notes.txt', { derived: { preview: 'images-1' } }));
    await scrollTo();

    expect(icon()).toBeInstanceOf(Element);
    expect(image()).toBeNull();
  });

  it('asks for a thumbnail that does not load once more, and keeps the icon when that does not load either', async () => {
    const sent = stubServer(async () => new Response(null, { headers: { ETag: '"made anew"' } }));
    await show(PHOTO);
    await scrollTo();
    await failed();
    await vi.waitFor(() => expect(image()!.getAttribute('src')).toContain('made+anew'));
    expect(sent).toHaveLength(1);

    await failed();
    await loaded();
    expect(icon()).toBeInstanceOf(Element);
    expect(image()!.classList.contains('hidden')).toBe(true);
    expect(sent).toHaveLength(1);
  });

  it.each<[string, ShelfFile, ContentSource, string]>([
    ['another is made', { ...PHOTO, derived: { thumbnail: 'images-2' } }, SOURCE, '/api/open/docs/files/derived?id=photo.jpg&kind=thumbnail&v=abc-images-2'],
    ['the contents change', { ...PHOTO, sha256: 'def' }, SOURCE, '/api/open/docs/files/derived?id=photo.jpg&kind=thumbnail&v=def-images-1'],
    ['it is read from elsewhere', PHOTO, liveContent('other'), '/api/open/other/files/derived?id=photo.jpg&kind=thumbnail&v=abc-images-1'],
  ])('loads another address once %s, showing the icon until that loads', async (_, changed, source, address) => {
    await show(PHOTO);
    await scrollTo();
    await loaded();

    await show(changed, source);

    expect(image()!.getAttribute('src')).toBe(address);
    expect(icon()).toBeInstanceOf(Element);
  });

  it('loads none once the file is encrypted', async () => {
    await show(PHOTO);
    await scrollTo();
    await loaded();

    await show({ ...PHOTO, encrypted: true, sha256: 'other', derived: null });

    expect(image()).toBeNull();
    expect(icon()).toBeInstanceOf(Element);
  });

  it('keeps showing the thumbnail of what it shows now when that of what it showed before arrives later', async () => {
    vi.stubGlobal('isSecureContext', false);
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => `blob:${(blob as Blob).size}`);
    vi.spyOn(URL, 'revokeObjectURL').mockReturnValue();
    const answers: ((response: Response) => void)[] = [];
    stubServer(() => new Promise((resolve) => { answers.push(resolve); }));
    const picture = (bytes: string) => new Response(bytes, { headers: { 'Content-Type': 'image/webp' } });
    await show(PHOTO);
    await scrollTo();
    await show({ ...PHOTO, sha256: 'def' });
    await vi.waitFor(() => expect(answers).toHaveLength(2));
    answers[1]!(picture('now'));
    await vi.waitFor(() => expect(image()!.getAttribute('src')).toBe('blob:3'));
    await loaded();

    answers[0]!(picture('before'));
    await act(async () => undefined);

    expect(image()!.getAttribute('src')).toBe('blob:3');
    expect(icon()).toBeNull();
  });

  it('fetches it with the header where the page keeps no cookie, and gives its blob up with the row', async () => {
    vi.stubGlobal('isSecureContext', false);
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('picture', { headers: { 'Content-Type': 'image/webp' } }))));
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:thumbnail');
    const revoked = vi.spyOn(URL, 'revokeObjectURL').mockReturnValue();
    await show(PHOTO);
    await scrollTo();
    await vi.waitFor(() => expect(image()!.getAttribute('src')).toBe('blob:thumbnail'));

    await show(shelfFile('notes.txt'));
    expect(revoked).toHaveBeenCalledWith('blob:thumbnail');
  });
});
