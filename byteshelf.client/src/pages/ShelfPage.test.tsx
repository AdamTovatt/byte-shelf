// @vitest-environment happy-dom
import { act, useEffect } from 'react';
import toast from 'react-hot-toast';
import type { QueryClient } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../lib/queryKeys';
import type { RunningPlugin } from '../lib/plugins/pluginHost';
import type { FileActionContext } from '../lib/plugins/fileAction';
import type { ShelfFile } from '../services/shelfService';
import { Modal } from '../ui';
import { encryptBlob } from '../lib/encryption/age';
import { removePdfViewer, restorePdfViewer, stubTabs } from '../test/browser';
import { button, dialogTitles, dimmingTitles, field, fill, labelled, press, shows, submitPassword, untilFound } from '../test/dom';
import { json, stubServer } from '../test/fakeServer';
import { stubIntersectionObserver } from '../test/intersectionObserver';
import type { pageHistory } from '../test/pageHistory';
import { shelfDirectory, shelfFile } from '../test/shelfFiles';
import { renderShelfPage, row, signIn } from '../test/shelfPage';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The plugins there are, and whether they are all loaded. */
const plugins = vi.hoisted(() => ({ state: { plugins: [] as RunningPlugin[], failures: new Map<string, string>() }, settled: true }));

vi.mock('../hooks/usePluginHost', () => ({
  usePluginHost: () => plugins.state,
  usePluginsSettled: () => plugins.settled,
  usePluginsAwaited: () => !plugins.settled,
}));

const LIMIT = 100 * 1024 * 1024;

/** The files in the shelf's root; a test adds to it to change what the next listing answers. */
let rootFiles: ShelfFile[];

/** The files a search found that are not in the root; a test adds to it. */
let elsewhere: ShelfFile[];

function listing(path: string) {
  return path === '/'
    ? { directory: shelfDirectory('/'), directories: [{ directory: shelfDirectory('/reports'), sizeBytes: 0, fileCount: 0, encryptedFileCount: 0 }], files: rootFiles }
    : { directory: shelfDirectory(path), directories: [], files: [] };
}

/** A group of `name` in the root and a copy of it in /reports. */
function duplicateGroup(name: string, sha256: string) {
  return { sha256, sizeBytes: 1, repeatedBytes: 1, files: [shelfFile(name), shelfFile(name, { id: `copy-${name}`, path: `/reports/${name}` })] };
}

/** What the search for duplicates answers: a page with a.txt's group, then, after its cursor, one with b.txt's. */
function duplicates(after: string | null) {
  if (findNothing) return { groups: [], nextCursor: null };
  return after === null
    ? { groups: [duplicateGroup('a.txt', 'a'.repeat(64))], nextCursor: `1:${'a'.repeat(64)}` }
    : { groups: [duplicateGroup('b.txt', 'b'.repeat(64))], nextCursor: null };
}

/** Whether the search for a second page fails, the type of the file it finds, and whether a third follows. */
let searchFails: boolean;
let secondPageType: string;
let hasThirdPage: boolean;

/** What the search for a page past the first, and a file's record, wait for before they are answered. */
let pageHeld: Promise<void> | null;
let recordHeld: Promise<void> | null;
/** Whether the next ask for a file's record fails. */
let recordFails: boolean;

/** An encrypted file's stored contents; null answers a plain one's. */
let stored: Blob | null;

/** Whether both searches find nothing; a test sets it to see what an empty search says. */
let findNothing: boolean;

/** What search answers: a page with /reports/c.txt, then, after its cursor, one with /reports/d.txt, and, when there is a third, one with /reports/e.txt. */
function searched(after: string | null) {
  if (findNothing) return { files: [], nextCursor: null };
  if (after === null) return { files: [shelfFile('c.txt', { path: '/reports/c.txt' })], nextCursor: 'next' };
  if (after === 'third') return { files: [shelfFile('e.txt', { path: '/reports/e.txt' })], nextCursor: null };
  return { files: [shelfFile('d.txt', { path: '/reports/d.txt', contentType: secondPageType })], nextCursor: hasThirdPage ? 'third' : null };
}

describe('the shelf page\'s rows', () => {
  let leave: () => void;
  let container: HTMLDivElement;
  let client: QueryClient;
  let sent: string[];
  let page: ReturnType<typeof pageHistory>;

  beforeEach(async () => {
    signIn();
    rootFiles = [shelfFile('a.txt'), shelfFile('b.txt')];
    elsewhere = [...['c.txt', 'd.txt', 'e.txt'].map((name) => shelfFile(name, { path: `/reports/${name}` })), duplicateGroup('a.txt', '').files[1]];
    searchFails = false;
    secondPageType = 'text/plain';
    hasThirdPage = false;
    pageHeld = null;
    recordHeld = null;
    recordFails = false;
    findNothing = false;
    stored = null;
    plugins.state = { plugins: [], failures: new Map() };
    plugins.settled = true;
    sent = stubServer(async (request) => {
      const url = new URL(request.url);
      if (url.pathname === '/api/shelves/docs/duplicates') return json(duplicates(url.searchParams.get('after')));
      if (url.pathname === '/api/shelves/docs/search' && url.searchParams.has('after')) await pageHeld;
      if (url.pathname === '/api/shelves/docs/search' && searchFails && url.searchParams.has('after')) return json({ message: 'Down.' }, 500);
      if (url.pathname === '/api/shelves/docs/search') return json(searched(url.searchParams.get('after')));
      if (url.pathname === '/api/shelves/docs/directories/encryption') return json({ encryptedBy: null });
      if (url.pathname === '/api/shelves/docs/content') return stored ? new Response(stored) : new Response('the contents', { status: 206 });
      if (url.pathname === '/api/shelves/docs/files') {
        await recordHeld;
        if (recordFails) {
          recordFails = false;
          return json({ message: 'Down.' }, 500);
        }
        const found = [...rootFiles, ...elsewhere].find((each) => each.id === url.searchParams.get('id'));
        return found ? json(found) : json({ message: 'There is no such file.', code: 'not_found' }, 404);
      }
      if (url.pathname === '/api/shelves/docs/files/metadata') return json({ readable: false, readAt: null });
      return url.pathname === '/api/shelves/docs/entries' ? json(listing(url.searchParams.get('path')!)) : json({ message: 'Not here.' }, 404);
    });
    await renderAt('/shelves/docs');
    await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
  });

  /** Opens the page at `url` in a tab of its own: no history and nothing cached. */
  async function renderAt(url: string): Promise<void> {
    ({ container, client, page, leave } = await renderShelfPage(url));
  }

  afterEach(() => {
    leave();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    restorePdfViewer();
  });

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

  it('shows a file\'s thumbnail in its row, and none in one without', async () => {
    vi.stubGlobal('isSecureContext', true);
    stubIntersectionObserver({ isInView: true });
    rootFiles = [shelfFile('photo.jpg', { derived: { thumbnail: 'images-1' } }), shelfFile('b.txt')];
    leave();
    await renderAt('/shelves/docs');

    await vi.waitFor(() => expect(row('photo.jpg')?.querySelector('img')?.getAttribute('src')).toContain('/api/open/docs/files/derived?id=photo.jpg&kind=thumbnail'));
    expect(row('b.txt')?.querySelector('img')).toBeNull();
  });

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

    expect(page.url).toBe('/shelves/docs/files/reports');
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

  /** Opens the search options and flips the filter labelled `label`; answers its row. */
  async function toggleFilter(label: string): Promise<HTMLButtonElement> {
    if (!document.querySelector('[role="group"][aria-label="Search options"]')) {
      click(container.querySelector('button[aria-label="Search options"]')!);
    }
    const toggle = await vi.waitFor(() => {
      const found = Array.from(document.querySelectorAll<HTMLButtonElement>('[aria-label="Search options"] button')).find((button) => button.textContent === label);
      expect(found).toBeDefined();
      return found!;
    });
    click(toggle);
    return toggle;
  }

  function toggleDuplicates(): Promise<HTMLButtonElement> {
    return toggleFilter('Duplicates only');
  }

  function showMoreButton(): HTMLButtonElement | undefined {
    return Array.from(container.querySelectorAll('button')).find((button) => button.textContent === 'Show more');
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

  it('searches for the files without a description once No description is on, with no text, a page at a time', async () => {
    const toggle = await toggleFilter('No description');

    await vi.waitFor(() => expect(container.textContent).toContain('reports/c.txt'));
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect(requests('GET /api/shelves/docs/search?under=%2F&hasDescription=false ')).toHaveLength(1);

    click(await vi.waitFor(() => {
      expect(showMoreButton()).toBeDefined();
      return showMoreButton()!;
    }));

    await vi.waitFor(() => expect(container.textContent).toContain('reports/d.txt'));
    expect(container.textContent).toContain('reports/c.txt');
    expect(requests('GET /api/shelves/docs/search?under=%2F&hasDescription=false&after=next ')).toHaveLength(1);
    expect(showMoreButton()).toBeUndefined();
  });

  it('sends No description with the text, and to the search for duplicates while Duplicates only is on', async () => {
    await toggleFilter('No description');
    type(searchField(), 'notes');
    await vi.waitFor(() => expect(requests('GET /api/shelves/docs/search?under=%2F&text=notes&hasDescription=false ')).toHaveLength(1));

    await toggleDuplicates();

    await vi.waitFor(() => expect(requests('GET /api/shelves/docs/duplicates?under=%2F&text=notes&hasDescription=false ')).toHaveLength(1));
  });

  it.each<[string[], string, string]>([
    [[], 'notes', 'No file here or below has “notes” in its name, description, title or camera.'],
    [['No description'], '', 'Every file here and below has a description.'],
    [['No description'], 'notes', 'No file here or below without a description has “notes” in its name, title or camera.'],
    [['Duplicates only'], '', 'No two files here or below have the same contents.'],
    [['Duplicates only'], 'notes', 'No files here or below with “notes” in their name, description, title or camera have the same contents.'],
    [['Duplicates only', 'No description'], '', 'No two files here or below without a description have the same contents.'],
    [['Duplicates only', 'No description'], 'notes', 'No files here or below without a description with “notes” in their name, title or camera have the same contents.'],
  ])('says what it looked for when %j with “%s” finds nothing', async (filters, text, said) => {
    findNothing = true;
    for (const label of filters) await toggleFilter(label);
    if (text) type(searchField(), text);

    await vi.waitFor(() => expect(container.textContent).toContain(said));
  });

  it('shows a file\'s details from its menu', async () => {
    click(container.querySelector('button[aria-label="Actions for a.txt"]')!);
    const details = Array.from(document.querySelectorAll('button')).find((button) => button.textContent === 'Details')!;

    click(details);

    await vi.waitFor(() => expect(document.body.textContent).toContain('The server does not read files of this type.'));
    expect(document.body.textContent).toContain('Details of a.txt');
    expect(sent).toContain('GET /api/shelves/docs/files/metadata?id=a.txt Bearer t');
  });

  /** The name of the file previewed, and where it says it stands; null when nothing is previewed. */
  function previewed(): { name: string; position: string | undefined } | null {
    const dialog = document.querySelector('[role="dialog"]');
    if (!dialog) return null;
    return { name: dialog.querySelector('h2')!.textContent!, position: dialog.textContent!.match(/\d+ of \d+\+?/)?.[0] };
  }

  function previews(expected: ReturnType<typeof previewed>): Promise<void> {
    return vi.waitFor(async () => {
      await act(async () => {});
      expect(previewed()).toEqual(expected);
    });
  }

  describe('with a plugin\'s file action', () => {
    let ran: FileActionContext[];
    let mounted: number;

    function Choosing() {
      useEffect(() => void mounted++, []);
      return <input aria-label="Format" />;
    }

    beforeEach(async () => {
      ran = [];
      mounted = 0;
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      plugins.state = {
        plugins: [{
          id: 'a',
          viewers: [],
          folderTypes: [],
          fileActions: [{
            id: 'broken', label: 'Broken', showsFor: () => true, run: () => undefined,
            icon: () => { throw new Error('It cannot be drawn.'); },
          }, {
            id: 'convert', label: 'Convert', showsFor: ({ name }) => name !== 'b.txt',
            run: (context) => {
              ran.push(context);
              context.showDialog((close) => <Modal isOpen title="Convert it" onClose={close}><Choosing /></Modal>);
            },
          }],
        }],
        failures: new Map(),
      };
      leave();
      await renderAt('/shelves/docs');
      await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
    });

    /** Opens the menu labelled `menu`, which no other may have been before it: one that closes is slow to go. */
    function openMenu(menu: string): void {
      click(document.querySelector(`button[aria-label="${menu}"]`)!);
    }

    it.each([['a.txt', true], ['b.txt', false], ['reports', false]])('offers it for the files it shows for, and no folder: %s, %s', (name, offered) => {
      openMenu(`Actions for ${name}`);

      expect(button('Rename')).not.toBeNull();
      expect(button('Convert') !== null).toBe(offered);
      // One that cannot be drawn is left out, alone.
      expect(button('Broken')).toBeNull();
    });

    it('offers it for what a search found', async () => {
      type(searchField(), 'notes');
      await vi.waitFor(() => expect(container.querySelector('button[aria-label="Actions for c.txt"]')).not.toBeNull());

      openMenu('Actions for c.txt');

      expect(button('Convert')).not.toBeNull();
    });

    it('runs it on that file of the shelf, and its dialog keeps the page\'s keys', async () => {
      openMenu('Actions for a.txt');
      await press('Convert');

      expect(ran.map(({ shelf, file: { name } }) => [shelf, name])).toEqual([['docs', 'a.txt']]);
      expect(dialogTitles()).toEqual(['Convert it']);
      key(document, { key: 'a', ctrlKey: true });
      expect(selected()).toEqual([]);

      key(document, { key: 'Escape' });
      expect(dialogTitles()).toEqual([]);
      key(document, { key: 'a', ctrlKey: true });
      expect(selected()).toHaveLength(3);
    });

    it('offers it in the preview, where Escape closes its dialog first', async () => {
      type(searchField(), 'notes');
      await vi.waitFor(() => expect(container.querySelector('button[aria-label="Open c.txt"]')).not.toBeNull());
      click(container.querySelector('button[aria-label="Open c.txt"]')!);
      await previews({ name: 'c.txt', position: '1 of 1+' });
      openMenu('More actions');
      await press('Convert');
      expect(ran.map(({ file: { name } }) => name)).toEqual(['c.txt']);
      expect(dialogTitles()).toEqual(['c.txt', 'Convert it']);
      // The preview dims the page already.
      expect(dimmingTitles()).toEqual(['c.txt']);

      key(document, { key: 'Escape' });
      expect(dialogTitles()).toEqual(['c.txt']);
    });

    it('keeps its dialog as it is, with the focus, when the preview closes under it', async () => {
      key(row('a.txt')!, { key: 'Enter' });
      await previews({ name: 'a.txt', position: '1 of 2' });
      openMenu('More actions');
      await press('Convert');
      const format = document.querySelector<HTMLInputElement>('input[aria-label="Format"]')!;
      format.value = 'PNG';
      act(() => format.focus());

      page.back();

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Convert it']));
      expect(document.querySelector('input[aria-label="Format"]')).toBe(format);
      expect(format.value).toBe('PNG');
      expect(document.activeElement).toBe(format);
      expect(mounted).toBe(1);
      key(document, { key: 'Escape' });
      expect(dialogTitles()).toEqual([]);
    });

    it('closes its dialog as another shelf is opened', async () => {
      openMenu('Actions for a.txt');
      await press('Convert');
      expect(dialogTitles()).toEqual(['Convert it']);

      page.to('/shelves/other');

      await vi.waitFor(() => expect(dialogTitles()).toEqual([]));
    });

    it('has the preview over its dialog as Forward opens it again, and Escape close it first', async () => {
      key(row('a.txt')!, { key: 'Enter' });
      await previews({ name: 'a.txt', position: '1 of 2' });
      openMenu('More actions');
      await press('Convert');
      page.back();
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Convert it']));

      page.forward();

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Convert it', 'a.txt']));
      key(document, { key: 'Escape' });
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Convert it']));
    });
  });

  it('opens a file whose viewer is unavailable here in the preview while plugins are loading, which may show it', async () => {
    removePdfViewer();
    const tabs = stubTabs();
    rootFiles = [shelfFile('a.pdf', { contentType: 'application/pdf' })];
    plugins.settled = false;
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.pdf')).not.toBeNull());

    key(row('a.pdf')!, { key: 'Enter' });

    await vi.waitFor(() => expect(previewed()?.name).toBe('a.pdf'));
    expect(tabs).toEqual([]);
  });

  it('opens a file no viewer takes in the preview while plugins are loading, which may take it', async () => {
    const tabs = stubTabs();
    rootFiles = [shelfFile('a.zip', { contentType: 'application/zip' })];
    plugins.settled = false;
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.zip')).not.toBeNull());

    key(row('a.zip')!, { key: 'Enter' });

    await previews({ name: 'a.zip', position: undefined });
    expect(tabs).toEqual([]);
  });

  it('previews a file on Enter, in the URL, and Back closes it', async () => {
    key(row('b.txt')!, { key: 'Enter' });
    await previews({ name: 'b.txt', position: '2 of 2' });
    expect(page.url).toBe('/shelves/docs?preview=b.txt');

    page.back();
    await previews(null);
    expect(page.url).toBe('/shelves/docs');
  });

  it('previews a file on a double-click', async () => {
    act(() => void row('a.txt')!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true })));
    await previews({ name: 'a.txt', position: '1 of 2' });
  });

  it('closes on Escape to where it was opened from, whatever it moved on to, keeping the selection', async () => {
    key(document, { key: 'a', ctrlKey: true });
    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });

    key(document, { key: 'ArrowRight' });
    await previews({ name: 'b.txt', position: '2 of 2' });
    expect(page.url).toBe('/shelves/docs?preview=b.txt');
    key(document, { key: 'Escape' });

    await previews(null);
    expect(page.url).toBe('/shelves/docs');
    expect(selected()).toEqual(['reports', 'a.txt', 'b.txt']);
  });

  it('goes only through the files a viewer takes', async () => {
    rootFiles = [shelfFile('a.txt'), shelfFile('a.zip', { contentType: 'application/zip' }), shelfFile('b.txt')];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.zip')).not.toBeNull());

    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });
    key(document, { key: 'ArrowRight' });
    await previews({ name: 'b.txt', position: '2 of 2' });
    key(document, { key: 'ArrowLeft' });
    await previews({ name: 'a.txt', position: '1 of 2' });
  });

  it('opens a file no viewer takes in a tab, as its menu opens any file', async () => {
    vi.stubGlobal('isSecureContext', true);
    const tabs = stubTabs();
    rootFiles = [shelfFile('a.txt'), shelfFile('a.zip', { contentType: 'application/zip' })];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.zip')).not.toBeNull());

    key(row('a.zip')!, { key: 'Enter' });
    await vi.waitFor(() => expect(tabs.map((tab) => tab.location.href)).toEqual(['/api/open/docs/content?id=a.zip']));
    click(container.querySelector('button[aria-label="Actions for a.txt"]')!);
    click(Array.from(document.querySelectorAll('button')).find((button) => button.textContent === 'Open in new tab')!);
    await vi.waitFor(() => expect(tabs.map((tab) => tab.location.href)).toContain('/api/open/docs/content?id=a.txt'));
    expect(previewed()).toBeNull();
  });

  /** Searches, and opens the result c.txt. */
  async function openResult(): Promise<void> {
    type(searchField(), 'notes');
    click(await untilFound('button[aria-label="Open c.txt"]'));
    await previews({ name: 'c.txt', position: '1 of 1+' });
  }

  const NEXT_PAGE = 'GET /api/shelves/docs/search?under=%2F&text=notes&after=next ';

  it('previews a search\'s files in their order, past the last one shown on to the next page', async () => {
    await openResult();
    expect(requests(NEXT_PAGE)).toEqual([]);

    key(document, { key: 'ArrowRight' });

    await previews({ name: 'd.txt', position: '2 of 2' });
    expect(requests(NEXT_PAGE)).toHaveLength(1);
  });

  it('goes past a page that holds nothing a viewer takes, on to the one after it', async () => {
    secondPageType = 'application/zip';
    hasThirdPage = true;
    await openResult();

    key(document, { key: 'ArrowRight' });

    await previews({ name: 'e.txt', position: '2 of 2' });
  });

  it('fetches the next page once, however often it is asked to move on while that loads', async () => {
    const said = vi.spyOn(toast, 'error').mockReturnValue('');
    let release = () => {};
    pageHeld = new Promise((resolve) => (release = resolve));
    await openResult();

    key(document, { key: 'ArrowRight' });
    await vi.waitFor(() => expect(requests(NEXT_PAGE)).toHaveLength(1));
    key(document, { key: 'ArrowRight' });
    release();

    await previews({ name: 'd.txt', position: '2 of 2' });
    expect([requests(NEXT_PAGE).length, said.mock.calls]).toEqual([1, []]);
  });

  it('stays closed when it is closed while the next page loads', async () => {
    let release = () => {};
    pageHeld = new Promise((resolve) => (release = resolve));
    await openResult();

    key(document, { key: 'ArrowRight' });
    await vi.waitFor(() => expect(requests(NEXT_PAGE)).toHaveLength(1));
    key(document, { key: 'Escape' });
    await previews(null);
    release();

    await untilFound('button[aria-label="Open d.txt"]');
    await act(async () => {});
    expect([page.url, previewed()]).toEqual(['/shelves/docs', null]);
  });

  it('stays on the last result when the last page holds nothing a viewer takes', async () => {
    secondPageType = 'application/zip';
    await openResult();

    key(document, { key: 'ArrowRight' });

    await previews({ name: 'c.txt', position: '1 of 1' });
  });

  it('stays on the last result when the next page fails, saying so, and asks again only when asked', async () => {
    const said = vi.spyOn(toast, 'error').mockReturnValue('');
    searchFails = true;
    await openResult();

    key(document, { key: 'ArrowRight' });
    await vi.waitFor(() => expect(requests(NEXT_PAGE)).toHaveLength(1));
    await vi.waitFor(() => expect(said.mock.calls).toEqual([['Down.']]));
    await previews({ name: 'c.txt', position: '1 of 1+' });
    searchFails = false;
    key(document, { key: 'ArrowRight' });

    await previews({ name: 'd.txt', position: '2 of 2' });
    expect(requests(NEXT_PAGE)).toHaveLength(2);
  });

  it('previews the files of a search for duplicates a group after another', async () => {
    await toggleDuplicates();
    click(await untilFound('button[aria-label="Open a.txt"]'));
    await previews({ name: 'a.txt', position: '1 of 2+' });

    key(document, { key: 'ArrowRight' });
    await previews({ name: 'a.txt', position: '2 of 2+' });
    key(document, { key: 'ArrowRight' });
    await previews({ name: 'b.txt', position: '3 of 4' });
  });

  it('offers Open in new tab only for what a tab shows', async () => {
    vi.stubGlobal('isSecureContext', false);
    rootFiles = [shelfFile('a.txt'), shelfFile('a.svg', { contentType: 'image/svg+xml' })];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.svg')).not.toBeNull());
    const items = (name: string) => {
      click(container.querySelector(`button[aria-label="Actions for ${name}"]`)!);
      return Array.from(document.querySelectorAll('button')).map((button) => button.textContent);
    };

    expect(items('a.svg')).not.toContain('Open in new tab');
    expect(items('a.txt')).toContain('Open in new tab');
  });

  it.each([
    ['a file of the folder among the others', 'a.txt', '1 of 2'],
    ['one elsewhere alone', 'c.txt', undefined],
  ])('keeps the preview of %s on a reload', async (_which, name, position) => {
    leave();

    await renderAt(`/shelves/docs?preview=${name}`);

    await previews({ name, position });
  });

  it('closes one a link opened by staying on the page', async () => {
    leave();
    await renderAt('/shelves/docs?preview=a.txt');
    await previews({ name: 'a.txt', position: '1 of 2' });

    key(document, { key: 'Escape' });

    await previews(null);
    expect(page.url).toBe('/shelves/docs');
  });

  it('offers the file\'s details', async () => {
    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });

    click(labelled('Details')!);

    await shows('Details of a.txt');
  });

  it('shows the listed record until its own loads', async () => {
    recordHeld = new Promise(() => {});

    key(row('b.txt')!, { key: 'Enter' });

    await previews({ name: 'b.txt', position: '2 of 2' });
    await shows('the contents');
  });

  it('asks again for a record that failed to load', async () => {
    leave();
    recordFails = true;

    await renderAt('/shelves/docs?preview=c.txt');

    await previews({ name: 'c.txt', position: undefined });
  });

  it.each([
    ['encrypted', false, 'This file is encrypted.'],
    ['decrypted', true, 'the contents'],
  ])('shows a file %s since it was previewed as it is listed now, not as it was', async (_how, encrypted, shown) => {
    sessionStorage.clear();
    const change = async (to: boolean) => {
      rootFiles = [shelfFile('a.txt'), shelfFile('b.txt', { encrypted: to })];
      await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    };
    await change(encrypted);
    key(row('a.txt')!, { key: 'Enter' });
    key(document, { key: 'ArrowRight' });
    await previews({ name: 'b.txt', position: '2 of 2' });
    key(document, { key: 'Escape' });
    await previews(null);

    await change(!encrypted);
    recordHeld = new Promise(() => {});
    key(row('a.txt')!, { key: 'Enter' });
    key(document, { key: 'ArrowRight' });

    await previews({ name: 'b.txt', position: '2 of 2' });
    await shows(shown);
  });

  describe('of an encrypted file', () => {
    beforeEach(async () => {
      sessionStorage.clear();
      rootFiles = [shelfFile('a.txt'), shelfFile('b.txt', { encrypted: true })];
      leave();
      await renderAt('/shelves/docs');
      await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
    });

    /** Waits for the preview to show the encrypted file locked, or asking for its password. */
    function locked(asking: boolean): Promise<void> {
      return vi.waitFor(async () => {
        await act(async () => {});
        expect(button('Unlock')).not.toBeNull();
        expect(document.querySelector('form') !== null).toBe(asking);
      });
    }

    it('asks for its password as it is opened, and no more once that is cancelled', async () => {
      key(row('b.txt')!, { key: 'Enter' });
      await locked(true);

      await press('Cancel');
      await locked(false);
      expect(previewed()).toEqual({ name: 'b.txt', position: '2 of 2' });
      await press('Unlock');
      await locked(true);
    });

    it('keeps the preview open as Escape cancels its prompt, which dims nothing more', async () => {
      key(row('b.txt')!, { key: 'Enter' });
      await locked(true);
      expect(dialogTitles()).toEqual(['b.txt', 'Unlock b.txt']);
      expect(dimmingTitles()).toEqual(['b.txt']);

      key(document, { key: 'Escape' });

      await locked(false);
      expect(previewed()).toEqual({ name: 'b.txt', position: '2 of 2' });
    });

    it('shows it locked when it is moved on to or the page reloaded, asking nothing', async () => {
      key(row('a.txt')!, { key: 'Enter' });
      await previews({ name: 'a.txt', position: '1 of 2' });
      key(document, { key: 'ArrowRight' });
      await previews({ name: 'b.txt', position: '2 of 2' });
      await locked(false);

      leave();
      await renderAt('/shelves/docs?preview=b.txt');
      await previews({ name: 'b.txt', position: '2 of 2' });
      await locked(false);
      expect(requests('GET /api/shelves/docs/content')).toHaveLength(1);
    });

    /** Presses Download in the preview of one too large to show unasked, which asks for its password; answers what is saved. */
    async function downloadLarge(): Promise<string[]> {
      stored = await encryptBlob(new Blob(['the secret']), 'pw', 'text/plain', 10);
      rootFiles = [shelfFile('a.txt'), shelfFile('b.txt', { encrypted: true, sizeBytes: LIMIT + 1 })];
      leave();
      await renderAt('/shelves/docs');
      await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
      const saved: string[] = [];
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        saved.push(this.download);
      });
      key(row('b.txt')!, { key: 'Enter' });
      await previews({ name: 'b.txt', position: '2 of 2' });
      click(document.querySelector('[role="dialog"] button[aria-label="Download"]')!);
      await vi.waitFor(() => expect(document.querySelector('form')).not.toBeNull());
      return saved;
    }

    it('is saved, and shown, by Download of one too large to show unasked, with one prompt for both', async () => {
      const saved = await downloadLarge();
      await submitPassword('pw');

      await vi.waitFor(async () => {
        await act(async () => {});
        expect(saved).toEqual(['b.txt']);
      });
      await vi.waitFor(() => expect(document.body.textContent).toContain('the secret'));
      expect(document.querySelector('form')).toBeNull();
    });

    it('keeps what was typed into the prompt a Download waits for as Back closes the preview', async () => {
      const saved = await downloadLarge();
      const prompt = document.querySelector('form')!;
      fill('Password', 'pw');

      page.back();

      await previews({ name: 'Unlock b.txt', position: undefined });
      expect(document.querySelector('form')).toBe(prompt);
      expect(field('Password').value).toBe('pw');
      await act(async () => prompt.requestSubmit());
      await vi.waitFor(async () => {
        await act(async () => {});
        expect(saved).toEqual(['b.txt']);
      });
    });

    it('asks over a dialog of a plugin\'s, and one inside that, dimming nothing more, and Escape is the prompt\'s', async () => {
      plugins.state = {
        plugins: [{
          id: 'a',
          viewers: [],
          folderTypes: [],
          fileActions: [{
            id: 'convert', label: 'Convert', showsFor: () => true,
            run: ({ content, showDialog }) => showDialog((close) => (
              <Modal isOpen title="Convert it" onClose={close}>
                <Modal isOpen title="Sure?" onClose={close}>
                  <button onClick={() => void content.blob().catch(() => undefined)}>Read it</button>
                </Modal>
              </Modal>
            )),
          }],
        }],
        failures: new Map(),
      };
      leave();
      await renderAt('/shelves/docs');
      await vi.waitFor(() => expect(row('b.txt')).not.toBeNull());
      click(document.querySelector('button[aria-label="Actions for b.txt"]')!);
      await press('Convert');
      await press('Read it');
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Convert it', 'Sure?', 'Unlock b.txt']));

      expect(dimmingTitles()).toEqual(['Convert it']);
      key(document, { key: 'Escape' });
      expect(dialogTitles()).toEqual(['Convert it', 'Sure?']);
    });

    it('takes its prompt back as Back closes the preview', async () => {
      key(row('b.txt')!, { key: 'Enter' });
      await locked(true);

      page.back();

      await previews(null);
      expect(document.querySelector('form')).toBeNull();
    });
  });

  it('opens a PDF in a tab where the browser has no viewer of its own for one, and still goes past it', async () => {
    vi.stubGlobal('isSecureContext', true);
    removePdfViewer();
    const tabs = stubTabs();
    rootFiles = [shelfFile('a.txt'), shelfFile('b.pdf', { contentType: 'application/pdf' })];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('b.pdf')).not.toBeNull());

    key(row('b.pdf')!, { key: 'Enter' });
    await vi.waitFor(() => expect(tabs.map((tab) => tab.location.href)).toEqual(['/api/open/docs/content?id=b.pdf']));
    expect(row('b.pdf')!.title).toContain('new tab');
    expect(row('a.txt')!.title).not.toContain('new tab');
    expect(previewed()).toBeNull();
    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });
    key(document, { key: 'ArrowRight' });
    await previews({ name: 'b.pdf', position: '2 of 2' });
  });

  it('closes the preview of a file that is gone, with a toast, and keeps that of one moved elsewhere', async () => {
    const said = vi.spyOn(toast, 'error').mockReturnValue('');
    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });

    elsewhere.push({ ...rootFiles[0], path: '/reports/a.txt' });
    rootFiles = rootFiles.slice(1);
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await previews({ name: 'a.txt', position: undefined });
    expect(said).not.toHaveBeenCalled();

    elsewhere.pop();
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await previews(null);
    expect(said).toHaveBeenCalledWith('That file is no longer here.', expect.anything());
    expect(page.url).toBe('/shelves/docs');
  });

  it('previews a file that was gone once it is back', async () => {
    vi.spyOn(toast, 'error').mockReturnValue('');
    const [back] = rootFiles;
    key(row('a.txt')!, { key: 'Enter' });
    await previews({ name: 'a.txt', position: '1 of 2' });
    rootFiles = rootFiles.slice(1);
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await previews(null);

    rootFiles = [back, ...rootFiles];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('a.txt')).not.toBeNull());
    key(row('a.txt')!, { key: 'Enter' });

    await previews({ name: 'a.txt', position: '1 of 2' });
    expect(toast.error).toHaveBeenCalledTimes(1);
  });

  it('does not ask again for a record that is not there', async () => {
    const asked = () => requests('GET /api/shelves/docs/files?id=none ').length;
    vi.spyOn(toast, 'error').mockReturnValue('');
    leave();

    await renderAt('/shelves/docs?preview=none');

    await previews(null);
    expect(asked()).toBe(1);
  });

  it('fetches the first subfolders ahead as a folder opens, and not again as its listing is refetched', async () => {
    await vi.waitFor(() => expect(listingRequests('/reports')).toBe(1));

    rootFiles = [...rootFiles, shelfFile('c.txt')];
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(row('c.txt')).not.toBeNull());

    expect(listingRequests('/')).toBe(2);
    expect(listingRequests('/reports')).toBe(1);
  });
});
