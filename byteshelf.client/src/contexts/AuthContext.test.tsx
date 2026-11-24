// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { AuthProvider, useAuth } from './AuthContext';
import { clearTokens, getFreshToken, hasSession, saveTokens } from '../services/authService';
import { folderPasswordKey, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The tokens of a session of the user `sub`, the `renewal`th of it. */
function tokensOf(sub: string, renewal = 1) {
  return { token: `h.${btoa(JSON.stringify({ sub, renewal }))}.s`, expiresAt: inAnHour(), refreshToken: `${sub}-${renewal}` };
}

/** Another tab saves a session, or ends it: heard here, as a tab that slept hears it, once it has. */
function heard(): void {
  act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'byteshelf_auth_refresh_token' })));
}

const key = folderPasswordKey('docs', 'folder-1');

describe('kept passwords and the session', () => {
  let root: Root;
  let client: QueryClient;
  let auth: ReturnType<typeof useAuth>;
  let reloaded: MockInstance;

  function Probe() {
    auth = useAuth();
    return null;
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    installFakeWebLocks();
    saveTokens(tokensOf('ada'));
    reloaded = vi.spyOn(window.location, 'reload').mockImplementation(() => {});
    stubServer(async () => new Response(null, { status: 204 }));
    rememberPassword(key, 'pw');
    await start();
  });

  /** Starts the app, as a page that is loaded does. */
  async function start(): Promise<void> {
    client = new QueryClient();
    root = createRoot(document.createElement('div'));
    await act(async () => root.render(<QueryClientProvider client={client}><AuthProvider><Probe /></AuthProvider></QueryClientProvider>));
  }

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('forgets them on sign-out, and starts the page anew once the session has ended', async () => {
    const sessions: boolean[] = [];
    reloaded.mockImplementation(() => void sessions.push(hasSession()));
    client.setQueryData(['held'], 'of the session');

    await act(() => auth.logout());

    expect(rememberedPassword(key)).toBeNull();
    expect(client.getQueryData(['held'])).toBeUndefined();
    expect(auth.isAuthenticated).toBe(false);
    expect(sessions).toEqual([false]);
  });

  it('forgets them on sign-in, so an account never inherits another\'s', () => {
    act(() => auth.login({ token: 'u', expiresAt: inAnHour(), refreshToken: 's' }));

    expect(rememberedPassword(key)).toBeNull();
  });

  it('keeps them when the app starts with a session', () => {
    expect(rememberedPassword(key)).toBe('pw');
  });

  it('forgets them when the app starts without a session, as a tab restored after a sign-out does', async () => {
    act(() => root.unmount());
    clearTokens();
    await start();

    expect(rememberedPassword(key)).toBeNull();
  });

  it('keeps them, and what is cached, as another tab renews the session', () => {
    client.setQueryData(['held'], 'of the session');

    saveTokens(tokensOf('ada', 2));
    heard();

    expect(rememberedPassword(key)).toBe('pw');
    expect(client.getQueryData(['held'])).toBe('of the session');
    expect(reloaded).not.toHaveBeenCalled();
  });

  it('keeps what is shown following what changes as another tab renews the session', async () => {
    let held = 'before';
    function Held() {
      return <p>{useQuery({ queryKey: ['held'], queryFn: async () => held }).data}</p>;
    }
    const container = document.createElement('div');
    const shown = createRoot(container);
    await act(async () => shown.render(<QueryClientProvider client={client}><Held /></QueryClientProvider>));
    await vi.waitFor(() => act(async () => expect(container.textContent).toBe('before')));

    saveTokens(tokensOf('ada', 2));
    heard();
    held = 'after';
    await act(() => client.invalidateQueries({ queryKey: ['held'] }));

    await vi.waitFor(() => act(async () => expect(container.textContent).toBe('after')));
    act(() => shown.unmount());
  });

  it('keeps what is cached as another tab stores something else', () => {
    client.setQueryData(['held'], 'of the session');
    clearTokens();

    act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'sidebar-width' })));

    expect(client.getQueryData(['held'])).toBe('of the session');
    expect(reloaded).not.toHaveBeenCalled();
  });

  it.each([
    ['signs out', () => clearTokens()],
    ['has signed in as someone else', () => saveTokens(tokensOf('bob'))],
  ])('forgets them and what is cached, and starts the page anew, when another tab %s', (_, change) => {
    client.setQueryData(['held'], 'of the session');

    change();
    heard();

    expect(rememberedPassword(key)).toBeNull();
    expect(client.getQueryData(['held'])).toBeUndefined();
    expect(auth.isAuthenticated).toBe(hasSession());
    expect(reloaded).toHaveBeenCalledTimes(1);
  });

  it('starts the page anew when another tab ends a session whose token names no one', () => {
    act(() => auth.login({ token: 'u', expiresAt: inAnHour(), refreshToken: 's' }));

    clearTokens();
    heard();

    expect(reloaded).toHaveBeenCalledTimes(1);
  });

  it('forgets them, and starts the page anew, when a renewal finds that the server ended the session', async () => {
    stubServer(async () => new Response(null, { status: 401 }));
    saveTokens({ ...tokensOf('ada'), expiresAt: new Date(0).toISOString() });

    await act(() => getFreshToken());

    expect(rememberedPassword(key)).toBeNull();
    expect(auth.isAuthenticated).toBe(false);
    expect(reloaded).toHaveBeenCalledTimes(1);
  });

  it('does not start the page anew as a request finds no session where no one is signed in', async () => {
    act(() => root.unmount());
    clearTokens();
    await start();

    await act(() => getFreshToken());

    expect(reloaded).not.toHaveBeenCalled();
  });

  it('goes on as another tab renews the session of someone who signed in here', () => {
    act(() => auth.login(tokensOf('bob')));

    saveTokens(tokensOf('bob', 2));
    heard();

    expect(reloaded).not.toHaveBeenCalled();
  });

  describe('where no one is signed in, as another tab signs in', () => {
    beforeEach(async () => {
      act(() => root.unmount());
      clearTokens();
      await start();
      saveTokens(tokensOf('bob'));
      heard();
    });

    it('shows the app, without starting anew', () => {
      expect(auth.isAuthenticated).toBe(true);
      expect(reloaded).not.toHaveBeenCalled();
    });

    it('goes on as that tab renews the session', () => {
      saveTokens(tokensOf('bob', 2));
      heard();

      expect(reloaded).not.toHaveBeenCalled();
    });
  });
});
