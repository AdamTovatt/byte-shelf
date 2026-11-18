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

/** A group of `name` in the root and a copy of it in /reports. */
function duplicateGroup(name: string, sha256: string) {
  return { sha256, sizeBytes: 1, repeatedBytes: 1, files: [file(name), { ...file(name), id: `file:copy-${name}`, path: `/reports/${name}` }] };
}

/** What the search for duplicates answers: a page with a.txt's group, then, after its cursor, one with b.txt's. */
function duplicates(after: string | null) {
  return after === null
    ? { groups: [duplicateGroup('a.txt', 'a'.repeat(64))], nextCursor: `1:${'a'.repeat(64)}` }
    : { groups: [duplicateGroup('b.txt', 'b'.repeat(64))], nextCursor: null };
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
      if (url.pathname === '/api/shelves/docs/duplicates') return json(duplicates(url.searchParams.get('after')));
      if (url.pathname === '/api/shelves/docs/files/metadata') return json({ readable: false, readAt: null });
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

  function searchField(): HTMLInputElement {
    return container.querySelector<HTMLInputElement>('input[aria-label="Search files in this folder and below"]')!;
  }

  /** Types into a field as a person does, which React hears as an input event. */
  function type(input: HTMLInputElement, text: string): void {
    act(() => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, text);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }

  /** Opens the search options and flips Duplicates only; answers its row. */
  async function toggleDuplicates(): Promise<HTMLButtonElement> {
    click(container.querySelector('button[aria-label="Search options"]')!);
    const toggle = await vi.waitFor(() => {
      const found = Array.from(document.querySelectorAll<HTMLButtonElement>('[aria-label="Search options"] button')).find((button) => button.textContent === 'Duplicates only');
      expect(found).toBeDefined();
      return found!;
    });
    click(toggle);
    return toggle;
  }

  function requests(prefix: string): string[] {
    return sent.filter((line) => line.startsWith(prefix));
  }

  it('shows the groups of files with the same contents once Duplicates only is on, with no text', async () => {
    const toggle = await toggleDuplicates();

    await vi.waitFor(() => expect(container.textContent).toContain('2 copies of 1 B, 1 B repeated'));
    expect(container.textContent).toContain('reports/a.txt');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(requests('GET /api/shelves/docs/duplicates?under=%2F ')).toHaveLength(1);
  });

  it('adds the next page of groups on Show more', async () => {
    await toggleDuplicates();
    const showMore = await vi.waitFor(() => {
      const found = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Show more');
      expect(found).toBeDefined();
      return found!;
    });

    click(showMore);

    await vi.waitFor(() => expect(container.textContent).toContain('reports/b.txt'));
    expect(container.textContent).toContain('reports/a.txt');
    expect(requests(`GET /api/shelves/docs/duplicates?under=%2F&after=1%3A${'a'.repeat(64)} `)).toHaveLength(1);
    expect(Array.from(container.querySelectorAll('button')).some((button) => button.textContent === 'Show more')).toBe(false);
  });

  it('sends text typed while Duplicates only is on to the search for duplicates, not to search', async () => {
    await toggleDuplicates();

    type(searchField(), 'notes');

    await vi.waitFor(() => expect(requests('GET /api/shelves/docs/duplicates?under=%2F&text=notes ')).toHaveLength(1));
    expect(requests('GET /api/shelves/docs/search')).toEqual([]);
  });

  it('keeps Duplicates only on when the text is cleared', async () => {
    const toggle = await toggleDuplicates();
    type(searchField(), 'notes');
    await vi.waitFor(() => expect(container.querySelector('button[aria-label="Clear the search"]')).not.toBeNull());

    click(container.querySelector('button[aria-label="Clear the search"]')!);

    expect(searchField().value).toBe('');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    await vi.waitFor(() => expect(container.textContent).toContain('2 copies of 1 B, 1 B repeated'));
  });

  it('brings the folder back when Duplicates only is turned off', async () => {
    const toggle = await toggleDuplicates();
    await vi.waitFor(() => expect(row('a.txt')).toBeNull());

    click(toggle);

    await vi.waitFor(() => expect(row('a.txt')).not.toBeNull());
    expect(container.textContent).not.toContain('copies of');
  });

  it('shows a file\'s details from its menu', async () => {
    click(container.querySelector('button[aria-label="Actions for a.txt"]')!);
    const details = Array.from(document.querySelectorAll('button')).find((button) => button.textContent === 'Details')!;

    click(details);

    await vi.waitFor(() => expect(document.body.textContent).toContain('The server does not read files of this type.'));
    expect(document.body.textContent).toContain('Details of a.txt');
    expect(sent).toContain('GET /api/shelves/docs/files/metadata?id=file%3Aa.txt Bearer t');
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
