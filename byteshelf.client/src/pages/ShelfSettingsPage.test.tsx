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
import type { ShelfLink } from '../services/linkService';
import type { UserInfo } from '../services/userService';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const ADMIN: UserInfo = { id: 'a', email: 'admin@example.com', displayName: 'Admin', roles: ['admin'] };
const MEMBER: UserInfo = { id: 'm', email: 'ada@example.com', displayName: 'Ada', roles: [] };

const SHARE_LINK: ShelfLink = {
  id: 'l1',
  kind: 'download',
  path: '/reports/q3.pdf',
  fileId: 'f',
  overwrite: false,
  description: null,
  createdBy: { kind: 'user', id: 'm', name: 'Ada', isCaller: true },
  createdAt: '2026-11-02T00:00:00Z',
  expiresAt: '2026-11-03T00:00:00Z',
};

const UPLOAD_LINK: ShelfLink = {
  id: 'l2',
  kind: 'upload',
  path: '/in/scan.pdf',
  fileId: null,
  overwrite: true,
  description: null,
  createdBy: { kind: 'key', id: 'k', name: 'backup job', isCaller: false },
  createdAt: '2026-11-02T00:00:00Z',
  expiresAt: '2026-11-02T00:15:00Z',
};

/** A share link to a file now in the trash, which has no path to show. */
const TRASHED_LINK: ShelfLink = { ...SHARE_LINK, id: 'l3', path: null, fileId: 'g' };

/** What the server holds for the shelf "docs", by path; the signed-in user is set per test. */
const ANSWERS: Record<string, unknown> = {
  '/api/shelves/docs': { id: 's', name: 'docs', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
  '/api/shelves/docs/access': [{ userId: 'u', email: 'ada@example.com', displayName: 'Ada', permission: 'read' }],
  '/api/shelves/docs/keys': [{ id: 'k', shelfId: 's', name: 'backup job', permission: 'addOnly', createdAt: '2026-11-02T00:00:00Z' }],
};

describe('a shelf\'s settings tabs', () => {
  let root: Root;
  let container: HTMLDivElement;
  let location: string;
  let me: UserInfo | null;
  let links: ShelfLink[];
  let revokeAnswer: () => Response;
  let sent: string[];

  function Location() {
    location = useLocation().pathname;
    return null;
  }

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    me = ADMIN;
    links = [SHARE_LINK, UPLOAD_LINK];
    revokeAnswer = () => new Response(null, { status: 204 });
    sent = stubServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/users/me') return me ? json(me) : json({ message: 'The server is down.' }, 503);
      if (path === '/api/shelves/docs/links') return json(links);
      if (request.method === 'DELETE' && path.startsWith('/api/shelves/docs/links/')) {
        const answer = revokeAnswer();
        if (answer.ok) links = links.filter((link) => !path.endsWith(`/${link.id}`));
        return answer;
      }

      const answer = ANSWERS[path];
      return answer === undefined ? json({ message: 'Not here.' }, 404) : json(answer);
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
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

  function tabs(): string[] {
    return [...container.querySelectorAll('[aria-label="Settings tabs"] button[aria-label]')].map((tab) => tab.getAttribute('aria-label')!);
  }

  it.each([
    ['general', 'General', 'Name and quota'],
    ['access', 'Access', 'ada@example.com'],
    ['keys', 'Keys', 'backup job'],
    ['links', 'Links', '/reports/q3.pdf'],
  ])('shows an admin the %s tab at its own route', async (tab, label, text) => {
    await open(`/shelves/docs/settings/${tab}`, text);

    expect(currentTab()).toBe(label);
    expect(location).toBe(`/shelves/docs/settings/${tab}`);
  });

  it.each(['/shelves/docs/settings', '/shelves/docs/settings/nothing-such'])('opens General for an admin from %s', async (url) => {
    await open(url, 'Name and quota');

    expect(location).toBe('/shelves/docs/settings/general');
    expect(currentTab()).toBe('General');
    expect(tabs()).toEqual(['General', 'Access', 'Keys', 'Links']);
  });

  it.each(['/shelves/docs/settings', '/shelves/docs/settings/keys'])('opens Links, the one tab shown, for someone not an admin from %s', async (url) => {
    me = MEMBER;

    await open(url, '/reports/q3.pdf');

    expect(location).toBe('/shelves/docs/settings/links');
    expect(tabs()).toEqual(['Links']);
  });

  it('lists each link with what it reaches, who made it and that it is a share or upload link', async () => {
    await open('/shelves/docs/settings/links', '/in/scan.pdf');

    expect(container.textContent).toContain('Share link · by Ada (you) · until ');
    expect(container.textContent).toContain('Upload link, replaces a file there · by backup job · until ');
  });

  it('says so when the shelf has no links', async () => {
    links = [];

    await open('/shelves/docs/settings/links', 'No links on this shelf.');

    expect(container.querySelector('[aria-label^="Revoke the link"]')).toBeNull();
  });

  it('revokes a link once confirmed, and lists it no more', async () => {
    await open('/shelves/docs/settings/links', '/reports/q3.pdf');

    await revoke('/reports/q3.pdf');

    await vi.waitFor(() => expect(container.textContent).not.toContain('/reports/q3.pdf'));
    expect(document.body.textContent).not.toContain('Revoke this link?');
    expect(sent).toContain('DELETE /api/shelves/docs/links/l1 Bearer t');
  });

  it('takes a link that ran out before it was revoked off the list, and closes the dialog', async () => {
    revokeAnswer = () => {
      links = [UPLOAD_LINK];
      return json({ message: 'There is no link l1 on the shelf \'docs\' that you may revoke.', code: 'not_found' }, 404);
    };
    await open('/shelves/docs/settings/links', '/reports/q3.pdf');

    await revoke('/reports/q3.pdf');

    await vi.waitFor(() => expect(container.textContent).not.toContain('/reports/q3.pdf'));
    expect(document.body.textContent).not.toContain('Revoke this link?');
  });

  it('keeps the dialog open with the server\'s reason when revoking fails', async () => {
    revokeAnswer = () => json({ message: 'The server is busy.' }, 503);
    await open('/shelves/docs/settings/links', '/reports/q3.pdf');

    await revoke('/reports/q3.pdf');

    await vi.waitFor(() => expect(document.body.textContent).toContain('The server is busy.'));
    expect(document.body.textContent).toContain('Revoke this link?');
    expect(container.textContent).toContain('/reports/q3.pdf');
  });

  it('names a share link to a file now in the trash', async () => {
    links = [TRASHED_LINK];

    await open('/shelves/docs/settings/links', 'A file in the trash');

    expect(container.querySelector('[aria-label="Revoke the link to A file in the trash"]')).not.toBeNull();
  });

  it('says why when who is signed in could not be loaded, rather than sending them to another tab', async () => {
    me = null;

    await open('/shelves/docs/settings/general', 'The server is down.');

    expect(location).toBe('/shelves/docs/settings/general');
  });

  /** Clicks a link's Revoke, then the dialog's. */
  async function revoke(place: string): Promise<void> {
    await act(async () => (container.querySelector(`[aria-label="Revoke the link to ${place}"]`) as HTMLButtonElement).click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('Revoke this link?'));
    const confirm = [...document.body.querySelectorAll('button')].find((button) => button.textContent === 'Revoke')!;
    await act(async () => confirm.click());
  }
});
