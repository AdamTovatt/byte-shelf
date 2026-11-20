// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SideNav } from './SideNav';
import { ThemeProvider } from '../../ui';
import { AuthProvider } from '../../contexts/AuthContext';
import { SidebarProvider } from '../../contexts/SidebarContext';
import { saveTokens } from '../../services/authService';
import { installFakeWebLocks } from '../../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the account menu', () => {
  let root: Root;

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  /** Renders the side nav against a server answering the release request with `release`, and opens the account menu. */
  async function openMenu(release: () => Response): Promise<string[]> {
    const sent = stubServer(async (request) => {
      switch (new URL(request.url).pathname) {
        case '/api/users/me':
          return json({ id: 'u1', email: 'ada@example.com', displayName: 'Ada', roles: [] });
        case '/api/shelves':
          return json([]);
        case '/api/version/release':
          return release();
        default:
          return json({ message: 'Not here.' }, 404);
      }
    });
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    await act(async () => root.render(
      <ThemeProvider>
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <AuthProvider>
              <SidebarProvider><SideNav /></SidebarProvider>
            </AuthProvider>
          </MemoryRouter>
        </QueryClientProvider>
      </ThemeProvider>,
    ));
    await vi.waitFor(() => expect(sent).toContain('GET /api/version/release Bearer first'));
    await vi.waitFor(() => expect(document.body.textContent).toContain('Ada'));
    // The innermost element reading the name: the click bubbles to the menu's trigger around it.
    const account = Array.from(document.body.querySelectorAll<HTMLElement>('*')).reverse().find((each) => each.textContent?.trim() === 'Ada');
    if (!account) throw new Error('No account menu.');
    await act(async () => account.click());
    return sent;
  }

  it("shows the server's release, asked for as the signed-in user", async () => {
    await openMenu(() => json({ version: '2.3.0', versionId: '0123456789abcdef0123456789abcdef' }));

    await vi.waitFor(() => expect(document.body.textContent).toContain('ByteShelf 2.3.0'));
  });

  it('still opens, without the release, when the server does not answer it', async () => {
    await openMenu(() => new Response(null, { status: 404 }));

    await vi.waitFor(() => expect(document.body.textContent).toContain('Log out'));
    expect(document.body.textContent).toContain('ada@example.com');
    expect(document.body.textContent).not.toContain('ByteShelf ');
  });
});
