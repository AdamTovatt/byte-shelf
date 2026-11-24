// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPage } from './SettingsPage';
import { ThemeProvider } from '../ui';
import { useCurrentUser } from '../hooks/useUserQueries';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { shows } from '../test/dom';
import { inAnHour, json, stubServer } from '../test/fakeServer';
import { pluginView } from '../test/plugins';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const plugins = { plugins: [pluginView('markdown', { version: '2.4.0' })], skipped: [] };

/** Drawn from the same answer about the user as the page, so once it is, so is what the page shows them. */
function Known() {
  const { data } = useCurrentUser();
  return data && <p>Signed in as {data.displayName}</p>;
}

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
    await act(async () => root.render(<ThemeProvider><QueryClientProvider client={client}><SettingsPage /><Known /></QueryClientProvider></ThemeProvider>));
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

    await shows('Signed in as Ada');
    expect(document.body.textContent).toContain('Passkeys');
    expect(document.body.textContent).not.toContain('Plugins');
    expect(sent).not.toContain('GET /api/plugins Bearer t');
  });
});
