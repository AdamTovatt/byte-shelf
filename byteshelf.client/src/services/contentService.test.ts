// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, stubServer } from '../test/fakeServer';
import { saveTokens } from './authService';
import { liveContent } from './contentService';

describe('a shelf\'s contents', () => {
  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
  });

  afterEach(() => vi.unstubAllGlobals());

  it.each([[5, 5], [5, 2]])('from %i up to %i are none, and not asked for', async (start, end) => {
    const sent = stubServer(async () => new Response('abcdef'));

    expect((await liveContent('docs').readRange('f', start, end)).size).toBe(0);
    expect(sent).toEqual([]);
  });

  it('are not read from an answer that carries none', async () => {
    stubServer(async () => new Response(null));

    await expect(liveContent('docs').open('f', new AbortController().signal)).rejects.toThrow('The server sent no contents.');
  });
});
