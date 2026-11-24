// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useLoadPlugins, usePluginHost } from './usePluginHost';
import { PluginHost, type PluginTransport } from '../lib/plugins/pluginHost';
import { definePlugin } from '../plugin/pluginDefinition';
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
});
