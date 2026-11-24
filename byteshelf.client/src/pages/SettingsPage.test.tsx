// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPage } from './SettingsPage';
import { ThemeProvider } from '../ui';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const plugins = { plugins: [{ id: 'markdown', name: 'Markdown', version: '2.4.0', moduleUrl: null, stylesheetUrl: null }], skipped: [] };

describe('the settings page', () => {
  let root: Root;
  let sent: string[];

  async function showTo(roles: string[]): Promise<void> {
    sent = stubServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/plugins') return json(plugins);
      if (path === '/api/users/me') return json({ id: 'u', email: 'ada@example.com', displayName: 'Ada', roles });
      return json({ available: true, canAdd: false, passkeys: [] });
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<ThemeProvider><QueryClientProvider client={client}><SettingsPage /></QueryClientProvider></ThemeProvider>));
    await vi.waitFor(() => expect(sent).toContain('GET /api/users/me Bearer t'));
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

  it('shows an admin the plugins', async () => {
    await showTo(['admin']);

    await vi.waitFor(() => expect(document.body.textContent).toContain('Version 2.4.0'));
  });

  it('shows anyone else none, and does not ask for them', async () => {
    await showTo([]);

    await act(() => new Promise((resolve) => setTimeout(resolve, 50)));
    expect(document.body.textContent).toContain('Passkeys');
    expect(document.body.textContent).not.toContain('Plugins');
    expect(sent).not.toContain('GET /api/plugins Bearer t');
  });
});
