// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from '../../services/authService';
import { deletedContent, liveContent } from '../../services/contentService';
import { installFakeWebLocks } from '../../test/fakeWebLocks';
import { inAnHour, stubServer } from '../../test/fakeServer';
import { shelfFile } from '../../test/shelfFiles';
import { derivedReader } from './derivedReader';

const PHOTO = shelfFile('f', { sha256: 'abc', derived: { thumbnail: 'images-1' } });

const LIVE = liveContent('docs');

/** As the server answers a HEAD of what is not made yet. */
const notReady = (retryAfter = '3') => new Response(null, { status: 503, headers: { 'Retry-After': retryAfter } });

/** As the server answers what was made, whose ETag means nothing to the page. */
const made = () => new Response('picture', { headers: { ETag: '"one of a kind"', 'Content-Type': 'image/webp' } });

const MADE_VERSION = encodeURIComponent('"one of a kind"').replaceAll('%20', '+');

/** Waits until the first request is answered, with the clock standing still. */
async function sentOnce(sent: string[]): Promise<void> {
  for (let turn = 0; turn < 100 || sent.length === 0; turn++) await vi.advanceTimersByTimeAsync(0);
  expect(sent).toHaveLength(1);
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  vi.stubGlobal('isSecureContext', true);
  localStorage.clear();
  installFakeWebLocks();
  saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('what plugins made from a file', () => {
  it('is loaded by an element at once when a listing named it, live or from the trash', async () => {
    const sent = stubServer(async () => made());

    expect(derivedReader(PHOTO, LIVE).has('thumbnail')).toBe(true);
    expect(await derivedReader(PHOTO, LIVE).url('thumbnail')).toBe('/api/open/docs/files/derived?id=f&kind=thumbnail&v=abc-images-1');
    expect(await derivedReader(PHOTO, deletedContent('docs', 'del', null)).url('thumbnail')).toBe('/api/open/docs/trash/del/derived?id=f&kind=thumbnail&v=abc-images-1');
    expect(sent).toEqual([]);
  });

  it('is waited for as long as the server says while it is being made', async () => {
    const answers = [notReady(), notReady(), made()];
    const sent = stubServer(async () => answers.shift()!);
    const reader = derivedReader(PHOTO, LIVE);

    expect(reader.has('full')).toBe(false);
    const url = reader.url('full');
    await sentOnce(sent);
    await vi.advanceTimersByTimeAsync(2_999);
    expect(sent).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);
    await vi.advanceTimersByTimeAsync(3_000);

    expect(await url).toBe(`/api/open/docs/files/derived?id=f&kind=full&v=${MADE_VERSION}`);
    expect(sent).toEqual(Array(3).fill('HEAD /api/shelves/docs/files/derived?id=f&kind=full Bearer t'));
  });

  it('is asked for again after a while when the server says when as a date', async () => {
    const answers = [notReady('Wed, 25 Nov 2026 12:00:00 GMT'), made()];
    const sent = stubServer(async () => answers.shift()!);
    const url = derivedReader(PHOTO, LIVE).url('full');
    await sentOnce(sent);
    await vi.advanceTimersByTimeAsync(4_999);
    expect(sent).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);

    expect(await url).not.toBeNull();
  });

  it.each([[503, {}], [500, { 'Retry-After': '3' }]])('fails at once on a %i with %o, which says nothing of its being made', async (status, headers) => {
    const sent = stubServer(async () => new Response(null, { status, headers }));

    await expect(derivedReader(PHOTO, LIVE).url('full')).rejects.toMatchObject({ status });
    expect(sent).toHaveLength(1);
  });

  it('fails at once on a 503 whose body names another error than its being made', async () => {
    vi.stubGlobal('isSecureContext', false);
    const body = JSON.stringify({ code: 'conflict', message: 'Something else.' });
    const sent = stubServer(async () => new Response(body, { status: 503, headers: { 'Retry-After': '3', 'Content-Type': 'application/json' } }));

    await expect(derivedReader(PHOTO, LIVE).url('full')).rejects.toMatchObject({ status: 503, code: 'conflict' });
    expect(sent).toHaveLength(1);
  });

  it('waits a second before asking again when the server says to ask again at once', async () => {
    const answers = [notReady('0'), made()];
    const sent = stubServer(async () => answers.shift()!);
    const url = derivedReader(PHOTO, LIVE).url('full');
    await sentOnce(sent);
    await vi.advanceTimersByTimeAsync(999);
    expect(sent).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(1);

    expect(await url).not.toBeNull();
    expect(sent).toHaveLength(2);
  });

  it('is asked of the server once the address a listing named did not load', async () => {
    const answers = [notReady(), made()];
    const sent = stubServer(async () => answers.shift()!);
    const reader = derivedReader(PHOTO, LIVE);
    expect(await reader.url('thumbnail')).toContain('v=abc-images-1');
    expect(await reader.url('thumbnail')).toContain('v=abc-images-1');

    reader.forget('thumbnail');
    const url = reader.url('thumbnail');
    await vi.waitFor(() => expect(sent).toHaveLength(1));
    await vi.advanceTimersByTimeAsync(3_000);

    expect(await url).toContain(`v=${MADE_VERSION}`);
    expect(sent).toEqual(Array(2).fill('HEAD /api/shelves/docs/files/derived?id=f&kind=thumbnail Bearer t'));
  });

  it('is what was asked for last, though what was asked for before it fails later', async () => {
    const answers: ((response: Response) => void)[] = [];
    stubServer(() => new Promise((resolve) => { answers.push(resolve); }));
    const reader = derivedReader(PHOTO, LIVE);
    const first = reader.url('full');
    reader.forget('full');
    const second = reader.url('full');
    await vi.waitFor(() => expect(answers).toHaveLength(2));
    answers[1]!(made());
    await second;

    answers[0]!(new Response(null, { status: 500 }));
    await expect(first).rejects.toThrow();

    expect(await reader.url('full')).toBe(await second);
    expect(answers).toHaveLength(2);
  });

  it('is asked for anew by whoever asks once it was closed', async () => {
    const answers = [made(), new Response('other', { headers: { ETag: '"other"' } }), new Response('third', { headers: { ETag: '"third"' } })];
    stubServer(async () => answers.shift()!);
    const reader = derivedReader(PHOTO, LIVE);
    expect(await reader.url('full')).toContain(`v=${MADE_VERSION}`);

    reader.close();
    expect(await reader.url('full')).toContain('v=%22other%22');

    reader.forget('thumbnail');
    reader.close();
    expect(await reader.url('thumbnail')).toContain('v=abc-images-1');
  });

  it('is null when nothing makes it, and asked for again by whoever asks again', async () => {
    const answers = [new Response(null, { status: 404 }), made()];
    stubServer(async () => answers.shift()!);
    const reader = derivedReader(PHOTO, LIVE);

    expect(await reader.url('full')).toBeNull();
    expect(await reader.url('full')).toContain(`v=${MADE_VERSION}`);
  });

  it('fails when the server does, as when the plugin could not run, and is asked for again by whoever asks again', async () => {
    const answers = [new Response(null, { status: 500 }), made()];
    stubServer(async () => answers.shift()!);
    const reader = derivedReader(PHOTO, LIVE);

    await expect(reader.url('full')).rejects.toThrow();
    expect(await reader.url('full')).toContain(`v=${MADE_VERSION}`);
  });

  it('is asked for once, however many wait, and no longer waited for by one that gives up', async () => {
    let answer!: (response: Response) => void;
    const sent = stubServer(() => new Promise((resolve) => { answer = resolve; }));
    const reader = derivedReader(PHOTO, LIVE);
    const givingUp = new AbortController();
    const first = reader.url('full', givingUp.signal);
    const second = reader.url('full');
    await vi.waitFor(() => expect(sent).toHaveLength(1));

    givingUp.abort();
    await expect(first).rejects.toMatchObject({ name: 'AbortError' });
    answer(made());

    expect(await second).not.toBeNull();
    expect(sent).toHaveLength(1);
  });

  it('is no longer waited for, nor asked for, once closed', async () => {
    const sent = stubServer(async () => notReady());
    const reader = derivedReader(PHOTO, LIVE);
    const url = reader.url('full');
    await vi.waitFor(() => expect(sent).toHaveLength(1));

    reader.close();

    await expect(url).rejects.toMatchObject({ name: 'AbortError' });
    await vi.advanceTimersByTimeAsync(60_000);
    expect(sent).toHaveLength(1);
  });

  it('is none of an encrypted file, whatever its record says, which is asked of no server', async () => {
    const sent = stubServer(async () => made());
    const reader = derivedReader(PHOTO, LIVE);
    reader.update({ ...PHOTO, encrypted: true });

    expect(reader.has('thumbnail')).toBe(false);
    expect(await reader.url('thumbnail')).toBeNull();
    expect(sent).toEqual([]);
  });

  it('is fetched with the token where the page keeps no cookie, and given up once closed', async () => {
    vi.stubGlobal('isSecureContext', false);
    const sent = stubServer(async () => made());
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:thumbnail');
    const revoked = vi.spyOn(URL, 'revokeObjectURL').mockReturnValue();
    const reader = derivedReader(PHOTO, LIVE);

    expect(await reader.url('thumbnail')).toBe('blob:thumbnail');
    expect(await reader.url('thumbnail')).toBe('blob:thumbnail');
    expect(sent).toEqual(['GET /api/shelves/docs/files/derived?id=f&kind=thumbnail Bearer t']);
    reader.close();

    expect(revoked).toHaveBeenCalledWith('blob:thumbnail');
  });

  it('is fetched past the browser\'s cache, and its bytes run nowhere as the app', async () => {
    vi.stubGlobal('isSecureContext', false);
    const caches: (RequestCache | undefined)[] = [];
    vi.stubGlobal('Request', class extends Request {
      constructor(input: RequestInfo | URL, init?: RequestInit) {
        super(input, init);
        caches.push(init?.cache);
      }
    });
    stubServer(async () => new Response('<svg/>', { headers: { 'Content-Type': 'image/svg+xml' } }));

    expect(await derivedReader(PHOTO, LIVE).url('thumbnail')).toMatch(/^data:image\/svg\+xml/);
    expect(caches).toEqual(['no-store']);
  });

  it('is fetched from the trash where the page keeps no cookie', async () => {
    vi.stubGlobal('isSecureContext', false);
    const sent = stubServer(async () => made());
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:thumbnail');

    await derivedReader(PHOTO, deletedContent('docs', 'del', null)).url('thumbnail');

    expect(sent).toEqual(['GET /api/shelves/docs/trash/del/derived?id=f&kind=thumbnail Bearer t']);
  });

  it('holds no blob of what arrives once closed', async () => {
    vi.stubGlobal('isSecureContext', false);
    let arrive!: (blob: Blob) => void;
    const response = made();
    vi.spyOn(response, 'blob').mockReturnValue(new Promise((resolve) => { arrive = resolve; }));
    stubServer(async () => response);
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:thumbnail');
    const reader = derivedReader(PHOTO, LIVE);
    const url = reader.url('thumbnail');
    await vi.waitFor(() => expect(response.blob).toHaveBeenCalled());

    reader.close();
    await expect(url).rejects.toMatchObject({ name: 'AbortError' });
    arrive(new Blob(['picture']));
    await vi.advanceTimersByTimeAsync(0);

    expect(created).not.toHaveBeenCalled();
  });
});
