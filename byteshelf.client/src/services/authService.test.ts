// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getFreshToken, renewAccessToken, saveTokens } from './authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';

const inAnHour = () => new Date(Date.now() + 60 * 60_000).toISOString();
const runOut = () => new Date(Date.now() - 1_000).toISOString();

function tokensResponse(token: string, refreshToken: string): Response {
  return new Response(JSON.stringify({ token, expiresAt: inAnHour(), refreshToken }), { status: 200 });
}

describe('session renewal', () => {
  const fetchMock = vi.fn<typeof fetch>();
  let lockRequests = new Map<string, number>();

  beforeEach(() => {
    localStorage.clear();
    fetchMock.mockReset();
    vi.stubGlobal('fetch', fetchMock);
    lockRequests = installFakeWebLocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('uses the access token while it works', async () => {
    saveTokens({ token: 'kept', expiresAt: inAnHour(), refreshToken: 'refresh-1' });

    expect(await getFreshToken()).toBe('kept');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('renews a token that has run out, once, however many requests ask at the same time', async () => {
    saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
    fetchMock.mockResolvedValue(tokensResponse('second', 'refresh-2'));

    const tokens = await Promise.all([getFreshToken(), getFreshToken(), getFreshToken()]);

    expect(tokens).toEqual(['second', 'second', 'second']);
    expect(lockRequests.get('byteshelf-token-renewal')).toBe(3);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-2');
  });

  it('takes up a token another tab renewed to instead of spending the refresh token again', async () => {
    saveTokens({ token: 'theirs', expiresAt: inAnHour(), refreshToken: 'refresh-2' });

    expect(await renewAccessToken('mine')).toBe('theirs');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('forgets a session the server has ended', async () => {
    saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
    fetchMock.mockResolvedValue(new Response(null, { status: 401 }));

    expect(await getFreshToken()).toBeNull();
    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
  });

  it('keeps the session when the server cannot renew it just now', async () => {
    saveTokens({ token: 'first', expiresAt: runOut(), refreshToken: 'refresh-1' });
    fetchMock.mockResolvedValue(new Response(null, { status: 503 }));

    expect(await getFreshToken()).toBeNull();
    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-1');
  });
});
