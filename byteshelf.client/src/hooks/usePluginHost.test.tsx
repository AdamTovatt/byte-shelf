// @vitest-environment happy-dom
import { act, useEffect } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useLoadPlugins, usePluginHost, usePluginsAwaited, usePluginsSettled } from './usePluginHost';
import { PluginHost, type PluginTransport } from '../lib/plugins/pluginHost';
import { definePlugin } from '../lib/plugins/pluginDefinition';
import { queryKeys } from '../lib/queryKeys';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// The app's one host, a new one for each test.
const page = vi.hoisted(() => ({ host: undefined as unknown }));
vi.mock('../lib/plugins/pluginHost', async (original) => ({
  ...await original<typeof import('../lib/plugins/pluginHost')>(),
  get pluginHost() {
    return page.host;
  },
}));

function Page({ isSignedIn }: { isSignedIn: boolean }) {
  useLoadPlugins(isSignedIn);
  return <p>{usePluginHost().plugins.map(({ id }) => id).join(' ')}</p>;
}

/** What waits for plugins, such as a preview, and everything that was drawn of it. */
let drawn: string[];

function Waiting() {
  useLoadPlugins(true);
  const settled = usePluginsSettled();
  const awaited = usePluginsAwaited();
  const ids = usePluginHost().plugins.map(({ id }) => id);
  useEffect(() => {
    drawn.push([settled ? 'settled' : 'loading', awaited && 'awaited', ...ids].filter(Boolean).join(' '));
  });
  return null;
}

describe('loading plugins into the page', () => {
  let root: Root;
  let sent: string[];
  let client: QueryClient;
  let importModule: ReturnType<typeof vi.fn<PluginTransport['importModule']>>;

  beforeEach(() => {
    importModule = vi.fn(async () => ({ default: definePlugin({}) }));
    page.host = new PluginHost({ importModule, loadStylesheet: () => ({ loaded: Promise.resolve(), remove: () => undefined }) });
    client = new QueryClient();
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    sent = stubServer(async () => json({ plugins: [{ id: 'late', name: 'Late', version: '1', moduleUrl: '/plugins/late/hash/plugin.js', stylesheetUrl: null }], skipped: null }));
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  async function show(isSignedIn: boolean): Promise<void> {
    await act(async () => root.render(<QueryClientProvider client={client}><Page isSignedIn={isSignedIn} /></QueryClientProvider>));
  }

  it('asks for none while signed out, and shows those that arrive once signed in', async () => {
    await show(false);
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(sent).toEqual([]);

    await show(true);
    await vi.waitFor(() => expect(document.body.textContent).toBe('late'));
    expect(importModule.mock.calls).toEqual([['/plugins/late/hash/plugin.js']]);
  });

  describe('waiting for them', () => {
    let answer: () => void;
    let failPlugins: boolean;

    beforeEach(() => {
      vi.useFakeTimers();
      drawn = [];
      failPlugins = false;
      client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      stubServer(async () => {
        await new Promise<void>((resolve) => { answer = resolve; });
        return failPlugins ? json({ message: 'Down.' }, 500) : json({ plugins: [{ id: 'late', name: 'Late', version: '1', moduleUrl: '/late.js', stylesheetUrl: null }, { id: 'server', name: 'Server', version: '1', moduleUrl: null, stylesheetUrl: null }], skipped: null });
      });
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    async function showWaiting(): Promise<void> {
      await act(async () => root.render(<QueryClientProvider client={client}><Waiting /></QueryClientProvider>));
      await act(() => vi.advanceTimersByTimeAsync(0));
    }

    async function after(milliseconds: number): Promise<string> {
      await act(() => vi.advanceTimersByTimeAsync(milliseconds));
      return drawn.at(-1)!;
    }

    it('ends as the last is loaded, with no moment between the list and the plugin', async () => {
      await showWaiting();
      expect(await after(1_000)).toBe('loading awaited');

      answer();

      expect(await after(100)).toBe('settled late');
      expect(drawn).not.toContain('settled');
      expect(drawn.filter((each) => each.startsWith('loading')).every((each) => each.includes('awaited'))).toBe(true);
    });

    it('ends when one fails', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      importModule.mockRejectedValue(new Error('It threw.'));
      await showWaiting();

      answer();

      expect(await after(100)).toBe('settled');
    });

    it('ends when which there are cannot be told', async () => {
      failPlugins = true;
      await showWaiting();

      answer();

      expect(await after(100)).toBe('settled');
    });

    it('is over for good once they are loaded, so one added later is not waited for', async () => {
      await showWaiting();
      answer();
      expect(await after(100)).toBe('settled late');

      importModule.mockReturnValue(new Promise(() => undefined));
      act(() => client.setQueryData(queryKeys.plugins.list, { plugins: [{ id: 'late', name: 'Late', version: '1', moduleUrl: '/late.js', stylesheetUrl: null }, { id: 'later', name: 'Later', version: '1', moduleUrl: '/later.js', stylesheetUrl: null }], skipped: null }));

      expect(await after(0)).toBe('loading late');
      expect(drawn).not.toContain('loading awaited late');
    });

    it('lasts 3 seconds at most, and one that comes after that is there all the same', async () => {
      let bring = (): void => undefined;
      importModule.mockReturnValue(new Promise((resolve) => { bring = () => resolve({ default: definePlugin({}) }); }));
      await showWaiting();
      answer();

      expect(await after(2_900)).toBe('loading awaited');
      expect(await after(200)).toBe('loading');

      bring();
      expect(await after(100)).toBe('settled late');
    });
  });
});
