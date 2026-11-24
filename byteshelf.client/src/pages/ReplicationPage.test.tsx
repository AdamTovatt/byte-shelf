// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { ReplicationPage } from './ReplicationPage';
import { AuthProvider } from '../contexts/AuthContext';
import { hasSession, saveTokens } from '../services/authService';
import { takeStartupToast } from '../lib/startupToast';
import { fill, press, shows } from '../test/dom';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('pairing with a primary', () => {
  let root: Root;
  let reloaded: MockInstance;
  let pairing: Response;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    reloaded = vi.spyOn(window.location, 'reload').mockImplementation(() => {});
    stubServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/replication/pair') return pairing;
      if (path === '/api/replication') return json({ replicas: [], source: null });
      return new Response(null, { status: 204 });
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><AuthProvider><ReplicationPage /></AuthProvider></QueryClientProvider>));
    await shows('Follow a primary');
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function pair(): Promise<void> {
    await press('Follow a primary');
    fill('Pairing code', 'code');
    fill('Name', 'backup');
    await press('Pair');
  }

  it('signs out, as the accounts here are replaced, and leaves what to do for the page that starts anew', async () => {
    pairing = json({ replicas: [], source: null });

    await pair();

    await vi.waitFor(() => expect(reloaded).toHaveBeenCalledTimes(1));
    expect(hasSession()).toBe(false);
    expect(takeStartupToast()).toContain('Sign in with your account on the primary');
  });

  it('says why it was refused, and stays signed in', async () => {
    pairing = json({ message: 'The code has run out.' }, 400);

    await pair();

    await shows('The code has run out.');
    expect(hasSession()).toBe(true);
    expect(takeStartupToast()).toBeNull();
    expect(reloaded).not.toHaveBeenCalled();
  });
});
