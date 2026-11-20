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
import type { ShelfKeyGrantInfo, ShelfKeyInfo } from '../services/accessService';
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

const DOCS_GRANT: ShelfKeyGrantInfo = { keyId: 'k', shelfId: 's', shelfName: 'docs', permission: 'addOnly', createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z' };
const PHOTOS_GRANT: ShelfKeyGrantInfo = { keyId: 'k', shelfId: 'p', shelfName: 'photos', permission: 'read', createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z' };

/** A key that works on docs and on photos. */
const KEY_ON_TWO_SHELVES: ShelfKeyInfo = { id: 'k', name: 'backup job', grants: [DOCS_GRANT, PHOTOS_GRANT], createdAt: '2026-11-02T00:00:00Z' };

/** The same key with docs its only shelf. */
const KEY_ON_DOCS_ONLY: ShelfKeyInfo = { ...KEY_ON_TWO_SHELVES, grants: [DOCS_GRANT] };

/** What the server holds for the shelf "docs", by path; the signed-in user and the keys are set per test. */
const ANSWERS: Record<string, unknown> = {
  '/api/shelves/docs': { id: 's', name: 'docs', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
  '/api/shelves/docs/access': [{ userId: 'u', email: 'ada@example.com', displayName: 'Ada', permission: 'read' }],
  '/api/shelves': [
    { id: 's', name: 'docs', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
    { id: 'p', name: 'photos', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
    { id: 'b', name: 'backups', quotaBytes: null, usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '2026-11-02T00:00:00Z' },
  ],
};

describe('a shelf\'s settings tabs', () => {
  let root: Root;
  let container: HTMLDivElement;
  let location: string;
  let me: UserInfo | null;
  let links: ShelfLink[];
  let revokeAnswer: () => Response;
  let keys: ShelfKeyInfo[];
  /** Each key grant set, as the request's path and the permission it carried. */
  let keyGrantsSet: string[];
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
    keys = [KEY_ON_TWO_SHELVES];
    keyGrantsSet = [];
    sent = stubServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (path === '/api/users/me') return me ? json(me) : json({ message: 'The server is down.' }, 503);
      if (path === '/api/shelves/docs/links') return json(links);
      if (request.method === 'DELETE' && path.startsWith('/api/shelves/docs/links/')) {
        const answer = revokeAnswer();
        if (answer.ok) links = links.filter((link) => !path.endsWith(`/${link.id}`));
        return answer;
      }

      if (path === '/api/shelves/docs/keys') return json(keys);
      if (request.method === 'PUT' && path.startsWith('/api/keys/')) {
        const { permission } = await request.json() as { permission: string };
        keyGrantsSet.push(`${path} ${permission}`);
        return json({ keyId: 'k', shelfId: 'b', shelfName: path.split('/').pop(), permission, createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z' });
      }

      if (request.method === 'DELETE' && path.startsWith('/api/keys/')) return new Response(null, { status: 204 });

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

  it('lists each key with what it may do on this shelf and the other shelves it works on', async () => {
    await open('/shelves/docs/settings/keys', 'backup job');

    expect(container.textContent).toContain('Add only · also on photos (read) · created ');
  });

  it.each([
    ['gives it a shelf it does not have', 'backups', 'Read', '/api/keys/k/grants/backups read'],
    ['changes its access on another shelf', 'photos', 'Read and write', '/api/keys/k/grants/photos readWrite'],
    ['changes its access on this shelf', 'docs', 'Read', '/api/keys/k/grants/docs read'],
  ])('%s as the access is picked in a key\'s Shelves dialog', async (_, shelf, choice, expected) => {
    await open('/shelves/docs/settings/keys', 'backup job');
    await openKeyShelves();

    await pick(shelf, choice);

    await vi.waitFor(() => expect(keyGrantsSet).toEqual([expected]));
  });

  it('takes a shelf from a key when None is picked in its Shelves dialog', async () => {
    await open('/shelves/docs/settings/keys', 'backup job');
    await openKeyShelves();

    await pick('photos', 'None');

    await vi.waitFor(() => expect(sent).toContain('DELETE /api/keys/k/grants/photos Bearer t'));
  });

  it('offers no None on a key\'s only shelf, which only its row\'s bin takes, asking first', async () => {
    keys = [KEY_ON_DOCS_ONLY];
    await open('/shelves/docs/settings/keys', 'backup job');
    await openKeyShelves();

    expect(choices('docs')).toEqual(['Read', 'Add only', 'Read and write']);
    expect(choices('photos')).toEqual(['None', 'Read', 'Add only', 'Read and write']);
  });

  it.each([
    ['takes a key off this shelf, once confirmed, when it has others', KEY_ON_TWO_SHELVES, 'Take the key backup job off docs?', 'Take off'],
    ['deletes a key whose only shelf this is, once confirmed', KEY_ON_DOCS_ONLY, 'Delete the key backup job?', 'Delete'],
  ])('%s', async (_, key, title, confirmLabel) => {
    keys = [key];
    await open('/shelves/docs/settings/keys', 'backup job');

    await act(async () => (container.querySelector('[aria-label="Take the key backup job off docs"]') as HTMLButtonElement).click());
    await vi.waitFor(() => expect(document.body.textContent).toContain(title));
    expect(sent.some((request) => request.startsWith('DELETE /api/keys/'))).toBe(false);
    await act(async () => buttonNamed(confirmLabel).click());

    await vi.waitFor(() => expect(sent).toContain('DELETE /api/keys/k/grants/docs Bearer t'));
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

  /** Opens the key's Shelves dialog and waits for the shelves to load in it. */
  async function openKeyShelves(): Promise<void> {
    await act(async () => (container.querySelector('[aria-label="The shelves the key backup job works on"]') as HTMLButtonElement).click());
    await vi.waitFor(() => expect(document.body.querySelector('[aria-label="The key\'s access to backups"]')).not.toBeNull());
  }

  function accessGroup(shelf: string): HTMLElement {
    return document.body.querySelector(`[aria-label="The key's access to ${shelf}"]`) as HTMLElement;
  }

  function choices(shelf: string): string[] {
    return [...accessGroup(shelf).querySelectorAll('button')].map((button) => button.textContent ?? '');
  }

  async function pick(shelf: string, choice: string): Promise<void> {
    const button = [...accessGroup(shelf).querySelectorAll('button')].find((candidate) => candidate.textContent === choice) as HTMLButtonElement;
    await act(async () => button.click());
  }

  function buttonNamed(label: string): HTMLButtonElement {
    return [...document.body.querySelectorAll('button')].find((button) => button.textContent === label) as HTMLButtonElement;
  }

  /** Clicks a link's Revoke, then the dialog's. */
  async function revoke(place: string): Promise<void> {
    await act(async () => (container.querySelector(`[aria-label="Revoke the link to ${place}"]`) as HTMLButtonElement).click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('Revoke this link?'));
    const confirm = [...document.body.querySelectorAll('button')].find((button) => button.textContent === 'Revoke')!;
    await act(async () => confirm.click());
  }
});
