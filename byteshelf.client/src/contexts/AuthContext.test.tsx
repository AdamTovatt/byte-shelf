// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './AuthContext';
import { clearTokens, saveTokens } from '../services/authService';
import { folderPasswordKey, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const key = folderPasswordKey('docs', 'folder-1');

describe('kept passwords and the session', () => {
  let root: Root;
  let auth: ReturnType<typeof useAuth>;

  function Probe() {
    auth = useAuth();
    return null;
  }

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    stubServer(async () => new Response(null, { status: 204 }));
    rememberPassword(key, 'pw');
    root = createRoot(document.createElement('div'));
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><AuthProvider><Probe /></AuthProvider></QueryClientProvider>));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('forgets them on sign-out', async () => {
    await act(() => auth.logout());

    expect(rememberedPassword(key)).toBeNull();
  });

  it('forgets them on sign-in, so an account never inherits another\'s', () => {
    act(() => auth.login({ token: 'u', expiresAt: inAnHour(), refreshToken: 's' }));

    expect(rememberedPassword(key)).toBeNull();
  });

  it('forgets them when another tab signs out, and keeps them when it only renews', () => {
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'byteshelf_auth_refresh_token' })));
    expect(rememberedPassword(key)).toBe('pw');

    clearTokens();
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: 'byteshelf_auth_refresh_token' })));
    expect(rememberedPassword(key)).toBeNull();
  });
});
