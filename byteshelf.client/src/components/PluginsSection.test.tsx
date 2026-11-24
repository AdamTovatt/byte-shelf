// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginsSection } from './PluginsSection';
import { PluginHost } from '../lib/plugins/pluginHost';
import { definePlugin } from '../lib/plugins/pluginDefinition';
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

describe('the plugins in settings', () => {
  let root: Root;
  let sent: string[];
  let host: PluginHost;

  /** Shows the section on a server answering the plugins with `answer`. */
  async function show(answer: () => Response): Promise<void> {
    sent = stubServer(async () => answer());
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<QueryClientProvider client={client}><PluginsSection /></QueryClientProvider>));
    await vi.waitFor(() => expect(client.isFetching()).toBe(0));
    expect(sent).toEqual(['GET /api/plugins Bearer t']);
  }

  function shows(text: string): Promise<void> {
    return vi.waitFor(() => expect(document.body.textContent).toContain(text));
  }

  beforeEach(() => {
    host = page.host = new PluginHost({
      importModule: async (url) => {
        if (url.includes('/broken/')) throw new Error('It threw.');
        return { default: definePlugin({}) };
      },
      loadStylesheet: () => ({ loaded: Promise.resolve(), remove: () => undefined }),
    });
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('is not there on a server without plugins', async () => {
    await show(() => json({ plugins: [], skipped: [] }));

    // Long enough for an answer to have been drawn, as the other tests wait for one.
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(document.body.textContent).toBe('');
  });

  it('lists each plugin with its version, and each skipped one with the reason', async () => {
    await show(() => json({
      plugins: [{ id: 'markdown', name: 'Markdown', version: '2.4.0', moduleUrl: null, stylesheetUrl: null }],
      skipped: [
        { folder: '/plugins/images', id: 'images', name: 'Images', version: '3.0.0', reason: 'It is for plugin api 2.' },
        { folder: '/plugins/nothing', id: null, name: null, version: null, reason: 'It has no plugin.json.' },
      ],
    }));

    await shows('Markdown');
    await shows('Version 2.4.0');
    await shows('Images');
    await shows('Version 3.0.0 · /plugins/images');
    await shows('Skipped: It is for plugin api 2.');
    await shows('/plugins/nothing');
    await shows('Skipped: It has no plugin.json.');
  });

  it('says why a plugin\'s part in the web UI failed, beside that plugin alone', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const plugins = ['broken', 'healthy'].map((id) => ({ id, name: id, version: '1.0.0', moduleUrl: `/plugins/${id}/hash/plugin.js`, stylesheetUrl: null }));
    await act(() => host.load(plugins));

    await show(() => json({ plugins, skipped: [] }));

    await shows('healthy');
    expect(document.body.textContent).toBe('Pluginsbroken' + 'Version 1.0.0' + 'Its part in the web UI failed: It threw.' + 'healthy' + 'Version 1.0.0');
  });

  it('says why when they cannot be loaded', async () => {
    await show(() => json({ message: 'The server is busy.' }, 503));

    await shows('The server is busy.');
  });
});
