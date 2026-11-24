// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getFreshToken, onSessionEnded, renewAccessToken, saveTokens, signOut } from './authService';
import { installFakeWebLocks, removeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

const runOut = () => new Date(Date.now() - 1_000).toISOString();

function tokens(token: string, refreshToken: string): Response {
  return json({ token, expiresAt: inAnHour(), refreshToken });
}

describe('the session', () => {
  let answer: (request: Request) => Promise<Response>;
  // Each request as "METHOD /path", and the refresh token each one carried.
  let sent: string[] = [];
  let spent: string[] = [];

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    answer = async () => new Response(null, { status: 500 });
    spent = [];
    sent = stubServer(async (request) => {
      spent.push(((await request.clone().json()) as { refreshToken?: string }).refreshToken ?? '');
      return answer(request);
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('renewal', () => {
    it('uses the access token while it works', async () => {
      saveTokens({ token: 'kept', expiresAt: inAnHour(), refreshToken: 'refresh-1' });

      expect(await getFreshToken()).toBe('kept');
      expect(sent).toEqual([]);
    });

    it('renews a token that has run out, once, however many requests ask at the same time', async () => {
      saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
      answer = async () => tokens('second', 'refresh-2');

      const renewed = await Promise.all([getFreshToken(), getFreshToken(), getFreshToken()]);

      expect(renewed).toEqual(['second', 'second', 'second']);
      expect([sent, spent]).toEqual([['POST /api/auth/refresh'], ['refresh-1']]);
      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-2');
    });

    it('renews once without Web Locks too, as over plain HTTP', async () => {
      removeWebLocks();
      saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
      answer = async () => tokens('second', 'refresh-2');

      const renewed = await Promise.all([getFreshToken(), getFreshToken(), getFreshToken()]);

      expect(renewed).toEqual(['second', 'second', 'second']);
      expect([sent, spent]).toEqual([['POST /api/auth/refresh'], ['refresh-1']]);
    });

    it('takes up a token another tab renewed to instead of spending the refresh token again', async () => {
      saveTokens({ token: 'theirs', expiresAt: inAnHour(), refreshToken: 'refresh-2' });

      expect(await renewAccessToken('mine')).toBe('theirs');
      expect(sent).toEqual([]);
    });

    it('forgets a session the server has ended', async () => {
      saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
      answer = async () => new Response(null, { status: 401 });

      expect(await getFreshToken()).toBeNull();
      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
    });

    it.each([
      ['the server has ended the session', runOut(), 401, 1],
      ['the server cannot renew it just now', runOut(), 503, 0],
      ['it has not run out', inAnHour(), 401, 0],
    ])('tells that it ended, or not, when %s', async (_, expiresAt, status, times) => {
      const told = vi.fn();
      const stop = onSessionEnded(told);
      saveTokens({ token: 'first', expiresAt, refreshToken: 'refresh-1' });
      answer = async () => new Response(null, { status });

      await getFreshToken();

      expect(told).toHaveBeenCalledTimes(times);
      stop();
    });

    it('tells that it ended when it is gone, and no longer once that is no one\'s to hear', async () => {
      const told = vi.fn();
      const stop = onSessionEnded(told);

      await getFreshToken();
      expect(told).toHaveBeenCalledTimes(1);

      stop();
      await getFreshToken();
      expect(told).toHaveBeenCalledTimes(1);
    });

    it('keeps the session when the server cannot renew it just now', async () => {
      saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
      answer = async () => new Response(null, { status: 503 });

      expect(await getFreshToken()).toBeNull();
      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-1');
    });
  });

  describe('signing out', () => {
    it('ends the session on the server and forgets it', async () => {
      saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
      answer = async () => new Response(null, { status: 204 });

      await signOut();

      expect([sent, spent]).toEqual([['POST /api/auth/logout'], ['refresh-1']]);
      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
      expect(localStorage.getItem('byteshelf_auth_token')).toBeNull();
    });

    it('ends it on the server also when the page starts anew meanwhile', async () => {
      saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
      const fetched = vi.fn(async () => new Response(null, { status: 204 }));
      vi.stubGlobal('fetch', fetched);

      await signOut();

      expect(fetched).toHaveBeenCalledWith('/api/auth/logout', expect.objectContaining({ keepalive: true }));
    });

    it('forgets the session even when the server cannot be reached', async () => {
      saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
      answer = () => Promise.reject(new TypeError('Failed to fetch'));

      await signOut();

      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
    });

    it('cannot be undone by a renewal that was under way', async () => {
      saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
      let answerRefresh: (response: Response) => void = () => undefined;
      answer = (request) => new URL(request.url).pathname === '/api/auth/refresh'
        ? new Promise<Response>((resolve) => { answerRefresh = resolve; })
        : Promise.resolve(new Response(null, { status: 204 }));

      const renewing = getFreshToken();
      await vi.waitFor(() => expect(sent).toEqual(['POST /api/auth/refresh']));
      const signingOut = signOut();
      answerRefresh(tokens('second', 'refresh-2'));
      await Promise.all([renewing, signingOut]);

      expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
      expect([sent, spent]).toEqual([['POST /api/auth/refresh', 'POST /api/auth/logout'], ['refresh-1', 'refresh-2']]);
    });
  });
});
