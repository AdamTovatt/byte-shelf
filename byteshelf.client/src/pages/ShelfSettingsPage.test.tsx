// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShelfSettingsPage } from './ShelfSettingsPage';
import { saveTokens } from '../services/authService';
import { SHELF_ROUTE_PATTERNS } from '../lib/shelfPaths';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** What the server holds for the shelf "docs", by path. */
const ANSWERS: Record<string, unknown> = {
  '/api/shelves/docs': { id: 's', name: 'docs', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
  '/api/shelves/docs/access': [{ userId: 'u', email: 'ada@example.com', displayName: 'Ada', permission: 'read' }],
  '/api/shelves/docs/keys': [{ id: 'k', shelfId: 's', name: 'backup job', permission: 'addOnly', createdAt: '2026-11-02T00:00:00Z' }],
};

describe('a shelf\'s settings tabs', () => {
  let root: Root;
  let container: HTMLDivElement;
  let location: string;

  function Location() {
    location = useLocation().pathname;
    return null;
  }

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    stubServer(async (request) => {
      const answer = ANSWERS[new URL(request.url).pathname];
      return answer === undefined ? json({ message: 'Not here.' }, 404) : json(answer);
    });
    container = document.createElement('div');
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  /** Opens `url` where App serves the settings page, and waits for `text` to show. */
  async function open(url: string, text: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[url]}>
          <Routes>
            <Route path={SHELF_ROUTE_PATTERNS.settings} element={<ShelfSettingsPage />} />
          </Routes>
          <Location />
        </MemoryRouter>
      </QueryClientProvider>,
    ));
    await vi.waitFor(() => expect(container.textContent).toContain(text));
  }

  function currentTab(): string | null {
    return container.querySelector('[aria-current="page"][aria-label]')?.getAttribute('aria-label') ?? null;
  }

  it.each([
    ['general', 'General', 'Name and quota'],
    ['access', 'Access', 'ada@example.com'],
    ['keys', 'Keys', 'backup job'],
  ])('shows the %s tab at its own route', async (tab, label, text) => {
    await open(`/shelves/docs/settings/${tab}`, text);

    expect(currentTab()).toBe(label);
    expect(location).toBe(`/shelves/docs/settings/${tab}`);
  });

  it.each(['/shelves/docs/settings', '/shelves/docs/settings/nothing-such'])('opens General from %s', async (url) => {
    await open(url, 'Name and quota');

    expect(location).toBe('/shelves/docs/settings/general');
    expect(currentTab()).toBe('General');
  });
});
