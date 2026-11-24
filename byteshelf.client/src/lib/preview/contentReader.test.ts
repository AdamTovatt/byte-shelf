// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from '../../services/authService';
import { deletedContent, liveContent } from '../../services/contentService';
import { installFakeWebLocks } from '../../test/fakeWebLocks';
import { inAnHour, stubServer } from '../../test/fakeServer';
import { blobReader, sourceReader } from './contentReader';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('a file read by its address', () => {
  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
  });

  it('is loaded by an element from where a tab opens it, live or from the trash', async () => {
    expect(await sourceReader('f', liveContent('docs')).url()).toBe('/api/open/docs/content?id=f');
    expect(await sourceReader('f', deletedContent('docs', 'del', null)).url()).toBe('/api/open/docs/trash/del/content?id=f');
  });

  it('is read whole, and in part, with the token', async () => {
    const ranges: (string | null)[] = [];
    const sent = stubServer(async (request) => {
      ranges.push(request.headers.get('Range'));
      return new Response('cde', { status: request.headers.has('Range') ? 206 : 200 });
    });
    const reader = sourceReader('f', liveContent('docs'));

    expect(await (await reader.blob()).text()).toBe('cde');
    expect(await (await reader.range(2, 5)).text()).toBe('cde');

    expect(sent).toEqual(['GET /api/shelves/docs/content?id=f Bearer t', 'GET /api/shelves/docs/content?id=f Bearer t']);
    expect(ranges).toEqual([null, 'bytes=2-4']);
  });

  it('is read in part from the trash, with the token', async () => {
    const sent = stubServer(async () => new Response('a', { status: 206 }));

    await sourceReader('f', deletedContent('docs', 'del', null)).range(0, 1);

    expect(sent).toEqual(['GET /api/shelves/docs/trash/del/content?id=f Bearer t']);
  });

  it('answers a part cut from the whole when that is what is sent, and nothing past the end', async () => {
    const answers = [new Response('abcdef'), new Response(null, { status: 416 })];
    stubServer(async () => answers.shift()!);
    const reader = sourceReader('f', liveContent('docs'));

    expect(await (await reader.range(2, 5)).text()).toBe('cde');
    expect((await reader.range(9, 12)).size).toBe(0);
  });

  it('aborts what is loading when its caller or the preview does, and reads again after', async () => {
    const aborted: string[] = [];
    let answering = false;
    const sent = stubServer((request) => (answering ? Promise.resolve(new Response('a')) : new Promise((_, reject) => {
      request.signal.addEventListener('abort', () => {
        aborted.push(request.url);
        reject(new DOMException('Aborted', 'AbortError'));
      });
    })));
    const reader = sourceReader('f', liveContent('docs'));
    const caller = new AbortController();

    const abandoned = reader.blob(caller.signal).catch((error: Error) => error.name);
    await vi.waitFor(() => expect(sent).toHaveLength(1));
    caller.abort();
    expect(await abandoned).toBe('AbortError');
    const closed = reader.range(0, 5, new AbortController().signal).catch((error: Error) => error.name);
    await vi.waitFor(() => expect(sent).toHaveLength(2));
    reader.close();
    expect(await closed).toBe('AbortError');
    expect(aborted).toHaveLength(2);

    answering = true;
    expect(await (await reader.blob()).text()).toBe('a');
  });
});

describe('a file read from its contents in the page', () => {
  it('is answered whole and in part, and has one URL, released once the preview closes', async () => {
    const revoked: string[] = [];
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url) => void revoked.push(url));
    const reader = blobReader(new Blob(['abcdef'], { type: 'image/png' }));

    expect(await (await reader.blob()).text()).toBe('abcdef');
    expect(await (await reader.range(2, 5)).text()).toBe('cde');
    const url = await reader.url();
    expect(await reader.url()).toBe(url);
    expect(revoked).toEqual([]);

    reader.close();

    await vi.waitFor(() => expect(revoked).toEqual([url]));
    expect(await reader.url()).not.toBe(url);
  });

  it('does not answer a URL that was released while it was made', async () => {
    const reader = blobReader(new Blob(['<svg/>'], { type: 'image/png' }));

    const url = reader.url().catch((error: Error) => error.name);
    reader.close();

    expect(await url).toBe('AbortError');
  });
});
