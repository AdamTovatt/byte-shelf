// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShelfPage } from './ShelfPage';
import { saveTokens } from '../services/authService';
import { SHELF_ROUTE_PATTERNS } from '../lib/shelfPaths';
import { queryKeys } from '../lib/queryKeys';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function directory(path: string) {
  return { id: `dir:${path}`, shelfId: 's', parentId: null, path, name: path.slice(path.lastIndexOf('/') + 1), createdAt: '2026-11-02T00:00:00Z', encrypted: false };
}

function file(name: string) {
  return {
    id: `file:${name}`, shelfId: 's', directoryId: 'dir:/', path: `/${name}`, name, contentType: 'text/plain',
    sizeBytes: 1, sha256: '', createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z', encrypted: false,
  };
}

/** The files in the shelf's root; a test adds to it to change what the next listing answers. */
let rootFiles: ReturnType<typeof file>[];

function listing(path: string) {
  return path === '/'
    ? { directory: directory('/'), directories: [{ directory: directory('/reports'), sizeBytes: 0, fileCount: 0, encryptedFileCount: 0 }], files: rootFiles }
    : { directory: directory(path), directories: [], files: [] };
}

describe('the shelf page\'s rows', () => {
  let root: Root;
  let container: HTMLDivElement;
  let client: QueryClient;
  let sent: string[];
  let location: string;

  function Location() {
    location = useLocation().pathname;
    return null;
  }

  beforeEach(async () => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    rootFiles = [file('a.txt'), file('b.txt')];
    sent = stubServer(async (request) => {
      const url = new URL(request.url);
      return url.pathname === '/api/shelves/docs/entries' ? json(listing(url.searchParams.get('path')!)) : json({ message: 'Not here.' }, 404);
    });
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={['/shelves/docs']}>
          <Routes>
            <Route path={SHELF_ROUTE_PATTERNS.shelf} element={<ShelfPage />} />
            <Route path={SHELF_ROUTE_PATTERNS.files} element={<ShelfPage />} />
          </Routes>
          <Location />
        </MemoryRouter>
      </QueryClientProvider>,
    ));
    await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  function row(name: string): HTMLButtonElement | null {
    return container.querySelector<HTMLButtonElement>(`[data-selectable-row] button[aria-label="${name}"]`);
  }

  /** The rows selected, by name. */
  function selected(): string[] {
    return Array.from(container.querySelectorAll('[data-selectable-row] button[aria-pressed="true"]'), (button) => button.getAttribute('aria-label')!);
  }

  function key(target: EventTarget, init: KeyboardEventInit): void {
    act(() => {
      target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
    });
  }

  function click(target: Element, init: MouseEventInit = {}): void {
    act(() => {
      target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
    });
  }

  function listingRequests(path: string): number {
    return sent.filter((line) => line.startsWith(`GET /api/shelves/docs/entries?path=${encodeURIComponent(path)} `)).length;
  }

  it.each([['ctrlKey'], ['metaKey']])('selects every row on %s and A', (modifier) => {
    key(document, { key: 'a', [modifier]: true });

    expect(selected()).toEqual(['reports', 'a.txt', 'b.txt']);
  });

  it('leaves the rows alone on Ctrl+A while the search field has the keys', () => {
    const search = container.querySelector<HTMLInputElement>('input[aria-label="Search files in this folder and below"]')!;
    search.focus();

    key(search, { key: 'a', ctrlKey: true });

    expect(selected()).toEqual([]);
  });

  it('clears the selection on Escape', () => {
    key(document, { key: 'a', ctrlKey: true });

    key(document, { key: 'Escape' });

    expect(selected()).toEqual([]);
  });

  it('opens a row on Enter', () => {
    key(row('reports')!, { key: 'Enter' });

    expect(location).toBe('/shelves/docs/files/reports');
  });

  it('toggles a row on Space, keeping the others', () => {
    click(row('a.txt')!);
    key(row('b.txt')!, { key: ' ' });
    expect(selected()).toEqual(['a.txt', 'b.txt']);

    key(row('a.txt')!, { key: ' ' });
    expect(selected()).toEqual(['b.txt']);
  });

  it('clears the selection on a click in empty space, but not on a click on a row', () => {
    click(row('a.txt')!);
    click(row('b.txt')!, { metaKey: true });
    expect(selected()).toEqual(['a.txt', 'b.txt']);

    click(container.querySelector('.min-h-full')!);

    expect(selected()).toEqual([]);
  });

  it('fetches the first subfolders ahead as a folder opens, and not again as its listing is refetched', async () => {
    await vi.waitFor(() => expect(listingRequests('/reports')).toBe(1));

    rootFiles = [...rootFiles, file('c.txt')];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('c.txt')).not.toBeNull());

    expect(listingRequests('/')).toBe(2);
    expect(listingRequests('/reports')).toBe(1);
  });
});
