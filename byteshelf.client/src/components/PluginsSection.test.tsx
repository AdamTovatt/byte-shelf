// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginsSection } from './PluginsSection';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the plugins in settings', () => {
  let root: Root;
  let sent: string[];

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
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
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

  it('says why when they cannot be loaded', async () => {
    await show(() => json({ message: 'The server is busy.' }, 503));

    await shows('The server is busy.');
  });
});
