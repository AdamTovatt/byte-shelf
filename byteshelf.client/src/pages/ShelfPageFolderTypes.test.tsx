// @vitest-environment happy-dom
import { act, lazy, useEffect, useState, type ComponentType, type SVGProps } from 'react';
import toast from 'react-hot-toast';
import { useQuery, type QueryClient } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { usePluginId } from '../contexts/PluginIdentity';
import { queryKeys } from '../lib/queryKeys';
import { createCheck, encryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword } from '../lib/encryption/passwords';
import type { FolderType, FolderViewerProps } from '../lib/plugins/folderType';
import type { RunningPlugin } from '../lib/plugins/pluginHost';
import type { ShelfFile } from '../services/shelfService';
import { stubTabs } from '../test/browser';
import { button, dialogTitles, labelled, press, shows, submitPassword, untilFound } from '../test/dom';
import { FAST } from '../test/encryptedShelf';
import { json, stubServer } from '../test/fakeServer';
import type { pageHistory } from '../test/pageHistory';
import { shelfDirectory, shelfFile } from '../test/shelfFiles';
import { renderShelfPage, row, signIn } from '../test/shelfPage';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

interface Plugins {
  plugins: RunningPlugin[];
  /** Whether they are waited for. */
  awaited: boolean;
}

/** The plugins there are, which the page follows. */
const host = vi.hoisted(() => {
  const listeners = new Set<() => void>();
  return {
    state: { plugins: [], awaited: false } as Plugins,
    set(changed: Partial<Plugins>): void {
      host.state = { ...host.state, ...changed };
      listeners.forEach((tell) => tell());
    },
    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      return () => void listeners.delete(listener);
    },
  };
});

vi.mock('../hooks/usePluginHost', async () => {
  const { useSyncExternalStore } = await import('react');
  const useHost = () => useSyncExternalStore(host.subscribe, () => host.state);
  return { usePluginHost: useHost, usePluginsSettled: () => !useHost().awaited, usePluginsAwaited: () => useHost().awaited };
});

/** A plugin with a type for albums. */
function plugin(component: ComponentType<FolderViewerProps>, changed: Partial<FolderType> = {}): RunningPlugin {
  return { id: 'a', viewers: [], fileActions: [], folderTypes: [{ suffix: 'album', label: 'Album', icon: () => <svg data-icon="album" />, component, ...changed }] };
}

function album(component: ComponentType<FolderViewerProps>, changed: Partial<FolderType> = {}): void {
  host.set({ plugins: [plugin(component, changed)] });
}

/** A component that loads as it is first drawn, once it is brought. */
function later<Props>() {
  const load = vi.fn<() => Promise<{ default: ComponentType<Props> }>>();
  let loaded: (module: { default: ComponentType<Props> }) => void = () => undefined;
  load.mockReturnValue(new Promise((resolve) => { loaded = resolve; }));
  return {
    component: lazy(load) as ComponentType<Props>,
    asked: () => vi.waitFor(() => expect(load).toHaveBeenCalled()),
    bring: (component: ComponentType<Props>) => act(async () => loaded({ default: component })),
  };
}

describe('a folder of a plugin\'s type', () => {
  let leave: () => void;
  let container: HTMLDivElement;
  let client: QueryClient;
  let sent: string[];
  let page: ReturnType<typeof pageHistory>;
  /** The folders there are, by path, and the files in each. */
  let folders: Map<string, { id?: string; files: ShelfFile[] }>;
  /** The check of /Trip.album's password; null while it is not encrypted. */
  let check: string | null;
  let stored: Map<string, Blob>;
  let info: { permission: string; onReplica: boolean };
  /** What the shelf's record waits for before it is answered. */
  let infoHeld: Promise<void> | null;
  let listingFails: boolean;
  /** What the viewer was last given, and how often one was mounted. */
  let given: FolderViewerProps;
  let mounted: number;

  function Album(props: FolderViewerProps) {
    given = props;
    useState(() => mounted++);
    const { data } = useQuery({ queryKey: [...props.contents.queryKey, 'all'], queryFn: ({ signal }) => props.contents.list(undefined, signal) });
    return <p data-testid="album">{data ? data.files.map(({ name }) => name).join(' ') : 'loading'}</p>;
  }

  function encryptedBy() {
    return check === null ? null : { directory: shelfDirectory('/Trip.album'), check };
  }

  beforeEach(() => {
    signIn();
    folders = new Map([
      ['/', { files: [] }],
      ['/reports', { files: [shelfFile('report.txt', { path: '/reports/report.txt' })] }],
      ['/Trip.album', { files: [shelfFile('beach.txt', { path: '/Trip.album/beach.txt' }), shelfFile('sea.txt', { path: '/Trip.album/sea.txt' })] }],
      ['/Trip.album/small', { files: [] }],
      ['/Trip.album/Deep.ALBUM', { files: [shelfFile('deep.txt', { path: '/Trip.album/Deep.ALBUM/deep.txt' })] }],
      ['/.album', { files: [] }],
    ]);
    check = null;
    stored = new Map();
    info = { permission: 'readWrite', onReplica: false };
    mounted = 0;
    album(Album);
    host.set({ awaited: false });
    listingFails = false;
    infoHeld = null;
    sent = stubServer(async (request) => {
      request.signal.throwIfAborted();
      const { pathname, searchParams } = new URL(request.url);
      const path = searchParams.get('path') ?? searchParams.get('under') ?? '';
      const at = folders.get(path);
      switch (pathname) {
        case '/api/shelves/docs':
          await infoHeld;
          return json({ id: 's', name: 'docs', usedBytes: 0, reservedBytes: 0, fileCount: 0, createdAt: '', quotaBytes: null, ...info });
        case '/api/shelves/docs/entries': {
          if (listingFails) return json({ message: 'Down.' }, 500);
          if (!at) return json({ message: 'There is no directory.', code: 'not_found' }, 404);
          const below = [...folders.keys()].filter((each) => each !== '/' && each.slice(0, each.lastIndexOf('/')) === (path === '/' ? '' : path));
          return json({
            directory: shelfDirectory(path, at.id ? { id: at.id } : {}), files: at.files, encryptedBy: path.startsWith('/Trip.album') ? encryptedBy() : null,
            directories: below.map((each) => ({ directory: shelfDirectory(each, folders.get(each)!.id ? { id: folders.get(each)!.id } : {}), sizeBytes: 0, fileCount: 0, encryptedFileCount: 0 })),
          });
        }
        case '/api/shelves/docs/tree': return json({ directory: shelfDirectory(path, at?.id ? { id: at.id } : {}), directories: [], files: at?.files ?? [], nextCursor: null });
        case '/api/shelves/docs/directories/encryption': return json({ encryptedBy: path.startsWith('/Trip.album') ? encryptedBy() : null });
        case '/api/shelves/docs/files': {
          const found = [...folders.values()].flatMap(({ files }) => files).find(({ id, path: there }) => (searchParams.has('id') ? id === searchParams.get('id') : there === path));
          return found ? json(found) : json({ message: 'There is no such file.', code: 'not_found' }, 404);
        }
        case '/api/shelves/docs/content': {
          if (request.method !== 'PUT') return new Response(stored.get(searchParams.get('id')!) ?? 'the contents', { status: 200 });
          const added = shelfFile(path.slice(path.lastIndexOf('/') + 1), { path });
          folders.get('/Trip.album')!.files.push(added);
          return json(added);
        }
        default: return json({ message: 'Not here.' }, 404);
      }
    });
  });

  afterEach(() => {
    leave();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** Opens the page at `url` in a tab of its own. */
  async function renderAt(url: string, options?: Parameters<typeof renderShelfPage>[1]): Promise<void> {
    ({ container, client, page, leave } = await renderShelfPage(url, options));
  }

  /** Fetches again what the page holds of the shelf. */
  async function refetch(): Promise<void> {
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
  }

  /** Waits until the folder at `path` has been listed, and what that shows. */
  async function listed(path: string): Promise<void> {
    await vi.waitFor(() => expect(client.getQueryData(queryKeys.shelfContents.listing('docs', path))).toBeDefined());
    await act(async () => {});
  }

  async function open(name: string): Promise<void> {
    await untilFound(`[data-selectable-row] button[aria-label="${name}"]`);
    act(() => void row(name)!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })));
  }

  async function chooseFrom(menu: string, item: string): Promise<void> {
    await act(async () => labelled(menu)!.click());
    await press(item);
  }

  function viewer(): Element | null {
    return container.querySelector('[data-testid="album"]');
  }

  function searchField(): Element | null {
    return container.querySelector('input[aria-label^="Search files"]');
  }

  describe('in a listing', () => {
    it('has its type\'s icon, which no other folder has', async () => {
      await renderAt('/shelves/docs');
      await untilFound('button[aria-label="Trip.album"]');

      const icons = ['Trip.album', 'reports', '.album'].map((name) => row(name)!.querySelector('[data-icon="album"]') !== null);

      expect(icons).toEqual([true, false, false]);
    });

    it('has a folder\'s icon where its own cannot be drawn', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      album(Album, { icon: () => { throw new Error('It cannot be drawn.'); } });
      await renderAt('/shelves/docs');
      await untilFound('button[aria-label="Trip.album"]');

      expect(row('Trip.album')!.firstElementChild!.outerHTML).toBe(row('reports')!.firstElementChild!.outerHTML);
    });

    it('has a folder\'s icon while its own loads, with the rows there meanwhile', async () => {
      const { component: icon, asked, bring } = later<SVGProps<SVGSVGElement>>();
      album(Album, { icon });
      await renderAt('/shelves/docs');
      await asked();
      await untilFound('button[aria-label="Trip.album"]');
      expect(row('Trip.album')!.firstElementChild!.outerHTML).toBe(row('reports')!.firstElementChild!.outerHTML);

      await bring(() => <svg data-icon="album" />);

      expect(row('Trip.album')!.querySelector('[data-icon="album"]')).not.toBeNull();
    });

    it('has the icon of another type once it is renamed to one, though its own could not be drawn', async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const [type] = plugin(Album, { icon: () => { throw new Error('It cannot be drawn.'); } }).folderTypes;
      host.set({ plugins: [{ ...plugin(Album), folderTypes: [type, { ...type, suffix: 'notebook', icon: () => <svg data-icon="notebook" /> }] }] });
      await renderAt('/shelves/docs');
      await untilFound('button[aria-label="Trip.album"]');
      folders.set('/Trip.notebook', { id: 'dir:/Trip.album', files: [] });
      folders.delete('/Trip.album');

      await refetch();

      expect((await untilFound('button[aria-label="Trip.notebook"]')).querySelector('[data-icon="notebook"]')).not.toBeNull();
    });

    it('offers its contents, which no other folder does', async () => {
      await renderAt('/shelves/docs');
      await untilFound('button[aria-label="Trip.album"]');
      await act(async () => labelled('Actions for reports')!.click());
      expect(button('Show contents')).toBeNull();

      await act(async () => labelled('Actions for Trip.album')!.click());
      expect(button('Show contents')).not.toBeNull();
    });
  });

  it('opens in its viewer, under the breadcrumbs, with nothing of the listing', async () => {
    await renderAt('/shelves/docs');
    await open('Trip.album');

    await shows('beach.txt sea.txt');
    expect(page.url).toBe('/shelves/docs/files/Trip.album');
    expect(container.textContent).toContain('Trip.album');
    expect([searchField(), button('Add'), container.querySelector('[data-selectable-row]'), container.querySelector('[data-testid="file-input"]')]).toEqual([null, null, null, null]);
  });

  it.each([['/shelves/docs/files/reports', 'report.txt'], ['/shelves/docs/files/.album', null], ['/shelves/docs/files/Trip.album/small', null]])('is not the plain one at %s, which has its listing', async (url, file) => {
    await renderAt(url);

    await shows(file ?? 'Nothing here yet');
    expect(searchField()).not.toBeNull();
    expect(viewer()).toBeNull();
  });

  it('opens below another in its own viewer', async () => {
    await renderAt('/shelves/docs/files/Trip.album?view=contents');
    await open('Deep.ALBUM');

    await shows('deep.txt');
    expect(page.url).toBe('/shelves/docs/files/Trip.album/Deep.ALBUM');
    expect(given.folder.path).toBe('/Trip.album/Deep.ALBUM');
  });

  it('opens in a listing without its plugin', async () => {
    host.set({ plugins: [] });
    await renderAt('/shelves/docs/files/Trip.album');

    await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
    expect(button('Show contents')).toBeNull();
  });

  it('waits for plugins still loading, which may have its type', async () => {
    host.set({ plugins: [], awaited: true });
    await renderAt('/shelves/docs/files/Trip.album');
    await listed('/Trip.album');
    expect([viewer(), row('beach.txt'), searchField()]).toEqual([null, null, null]);

    act(() => host.set({ plugins: [plugin(Album)], awaited: false }));

    await shows('beach.txt sea.txt');
    expect(searchField()).toBeNull();
  });

  it('waits for a plugin that comes first of two with its type', async () => {
    const first = { ...plugin(() => <p>the first</p>), id: '0' };
    host.set({ plugins: [plugin(Album)], awaited: true });
    await renderAt('/shelves/docs/files/Trip.album');
    await listed('/Trip.album');

    act(() => host.set({ plugins: [first, plugin(Album)], awaited: false }));

    await shows('the first');
    expect(mounted).toBe(0);
  });

  it('has its listing once that is waited for no more', async () => {
    host.set({ plugins: [], awaited: true });
    await renderAt('/shelves/docs/files/Trip.album');
    await listed('/Trip.album');

    act(() => host.set({ awaited: false }));

    await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
  });

  describe('whose plugin loads once its listing shows', () => {
    beforeEach(async () => {
      host.set({ plugins: [] });
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      await chooseFrom('Actions for beach.txt', 'Rename');
      act(() => host.set({ plugins: [plugin(Album)] }));
    });

    it('keeps the listing, and what is open over it', () => {
      expect(dialogTitles()).toEqual(['Rename beach.txt']);
      expect([row('beach.txt') !== null, viewer()]).toEqual([true, null]);
    });

    it('shows in the viewer from the page\'s menu', async () => {
      await press('Cancel');
      await chooseFrom('Pages of docs', 'Show as Album');

      await shows('beach.txt sea.txt');
      expect(viewer()).not.toBeNull();
    });

    it('shows in the viewer as it is opened again', async () => {
      await press('Cancel');
      await chooseFrom('Pages of docs', 'Files');
      await open('Trip.album');

      await shows('beach.txt sea.txt');
      expect(viewer()).not.toBeNull();
    });
  });

  it('keeps its viewer as a plugin that comes before its own loads', async () => {
    await renderAt('/shelves/docs/files/Trip.album');
    await shows('beach.txt sea.txt');

    act(() => host.set({ plugins: [{ ...plugin(() => <p>the first</p>), id: '0' }, plugin(Album)] }));

    expect(viewer()).not.toBeNull();
    expect(mounted).toBe(1);
  });

  describe('that cannot be listed as it opens', () => {
    beforeEach(async () => {
      listingFails = true;
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('Down.');
    });

    it('says why, in place of its viewer', () => {
      expect([viewer(), searchField() !== null]).toEqual([null, true]);
    });

    it('shows in its viewer once it can be', async () => {
      listingFails = false;

      await refetch();

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });
  });

  it('opens in its viewer by a link with another view', async () => {
    await renderAt('/shelves/docs/files/Trip.album?view=other');

    await shows('beach.txt sea.txt');
    expect(viewer()).not.toBeNull();
  });

  it.each(['/shelves/docs/files/reports', '/shelves/docs/files/Trip.album?view=contents'])('waits for no plugin where none could show it otherwise: %s', async (url) => {
    host.set({ awaited: true });
    await renderAt(url);

    await vi.waitFor(() => expect(searchField()).not.toBeNull());
  });

  it('is drawn by a component that loads as it is shown, under the breadcrumbs and a spinner meanwhile', async () => {
    const { component, asked, bring } = later<FolderViewerProps>();
    album(component);
    await renderAt('/shelves/docs/files/Trip.album');
    await asked();
    const header = labelled('Pages of docs');
    expect([header !== null, viewer()]).toEqual([true, null]);
    expect(header!.closest('.min-h-full')!.querySelector('[data-spinner]')).not.toBeNull();

    await bring(Album);

    await shows('beach.txt sea.txt');
    expect(labelled('Pages of docs')).toBe(header);
  });

  it('leaves the listing when its viewer throws', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    album(() => { throw new Error('It failed.'); });
    await renderAt('/shelves/docs/files/Trip.album');

    await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
    expect(searchField()).not.toBeNull();
    await act(async () => labelled('Pages of docs')!.click());
    expect(button('Show as Album')).not.toBeNull();
  });

  describe('whose viewer threw', () => {
    let fails: boolean;
    let drawn: number;

    beforeEach(async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      fails = true;
      drawn = 0;
      album((props) => {
        drawn++;
        if (fails) throw new Error('It failed.');
        return <Album {...props} />;
      });
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      fails = false;
    });

    it('keeps its listing as it is listed again', async () => {
      await refetch();

      expect([row('beach.txt') !== null, viewer()]).toEqual([true, null]);
    });

    it('keeps its listing, asking for no password, as it is encrypted', async () => {
      check = await createCheck('right', FAST);
      folders.get('/Trip.album')!.files.push(shelfFile('late.txt', { path: '/Trip.album/late.txt' }));

      await refetch();
      // Shown by the listing that says the folder is encrypted.
      await untilFound('[data-selectable-row] button[aria-label="late.txt"]');
      await act(async () => {});

      expect([row('beach.txt') !== null, viewer(), dialogTitles()]).toEqual([true, null, []]);
    });

    it('tries it again from the page\'s menu', async () => {
      await chooseFrom('Pages of docs', 'Show as Album');

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    it('tries it again as its contents are left for it', async () => {
      page.to('/shelves/docs/files/Trip.album?view=contents');
      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      await chooseFrom('Pages of docs', 'Show as Album');

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    it('keeps its listing as it fails again', async () => {
      fails = true;
      const before = drawn;
      await chooseFrom('Pages of docs', 'Show as Album');

      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      expect(viewer()).toBeNull();
      expect(drawn).toBeGreaterThan(before);
    });

    it('tries it again as it is opened again', async () => {
      await chooseFrom('Pages of docs', 'Files');
      await open('Trip.album');

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    it('shows in it another folder that takes its place', async () => {
      folders.get('/Trip.album')!.id = 'another';

      await refetch();

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      expect(given.folder.id).toBe('another');
    });
  });

  describe('whose viewer throws as it goes', () => {
    let fails: boolean;

    beforeEach(async () => {
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      fails = true;
      album((props) => {
        useEffect(() => () => {
          if (fails) throw new Error('It failed as it went.');
        }, []);
        return <Album {...props} />;
      });
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    // Nothing is above the page here, as the app is elsewhere.
    afterEach(() => {
      fails = false;
    });

    it('has its listing once its contents are asked for', async () => {
      await chooseFrom('Pages of docs', 'Show contents');

      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
    });

    it('asks for its password as it is encrypted, and shows in its viewer', async () => {
      check = await createCheck('right', FAST);

      await refetch();
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      await submitPassword('right');

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    it('asks for its password as that changes, and shows in its viewer as its encryption is removed', async () => {
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'right');
      check = await createCheck('right', FAST);
      await refetch();
      await vi.waitFor(() => expect(client.isFetching()).toBe(0));
      check = await createCheck('another', FAST);
      await refetch();
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      await submitPassword('another');
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      const before = mounted;
      check = null;

      await refetch();

      await vi.waitFor(() => expect(mounted).toBeGreaterThan(before));
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
    });

    it('gives way to the viewer of another folder that takes its place', async () => {
      folders.get('/Trip.album')!.id = 'another';

      await refetch();

      await vi.waitFor(() => expect(given.folder.id).toBe('another'));
      expect(viewer()).not.toBeNull();
    });

    it('gives way to another folder', async () => {
      page.to('/shelves/docs/files/Trip.album/Deep.ALBUM');

      await shows('deep.txt');
    });
  });

  it('leaves the rows there as one goes whose icon throws as it goes, as the others\' would', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    let drawn = 0;
    function Failing() {
      useEffect(() => {
        drawn++;
        return () => {
          // Not for ever, which would be tried for as long.
          if (drawn < 50) throw new Error('It failed as it went.');
        };
      }, []);
      return <svg data-icon="album" />;
    }
    folders.set('/Other.album', { files: [] });
    folders.set('/Third.album', { files: [] });
    album(Album, { icon: Failing });
    await renderAt('/shelves/docs');
    await untilFound('button[aria-label="Third.album"]');
    folders.delete('/Third.album');

    await refetch();

    await vi.waitFor(() => expect(row('Third.album')).toBeNull());
    expect([row('Trip.album'), row('Other.album')]).not.toContain(null);
    // Three rows, and the two left once more.
    expect(drawn).toBe(5);
    drawn = 50;
  });

  it('leaves the viewer of another folder when one throws', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    album((props) => {
      if (props.folder.path === '/Trip.album') throw new Error('It failed.');
      return <Album {...props} />;
    });
    await renderAt('/shelves/docs/files/Trip.album');
    await open('Deep.ALBUM');

    await vi.waitFor(() => expect(viewer()?.textContent).toBe('deep.txt'));
  });

  describe('its contents', () => {
    it('show as a listing from its row, which Back leaves', async () => {
      await renderAt('/shelves/docs');
      await untilFound('button[aria-label="Trip.album"]');
      await chooseFrom('Actions for Trip.album', 'Show contents');

      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      expect(page.url).toBe('/shelves/docs/files/Trip.album?view=contents');

      page.back();
      expect(page.url).toBe('/shelves/docs');
    });

    it('show as a listing from the page\'s menu, which Back leaves for the viewer', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      await chooseFrom('Pages of docs', 'Show contents');
      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());

      page.back();
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      expect(page.url).toBe('/shelves/docs/files/Trip.album');
    });

    it('show in the viewer again from the page\'s menu', async () => {
      await renderAt('/shelves/docs/files/Trip.album?view=contents');
      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      await chooseFrom('Pages of docs', 'Show as Album');

      await shows('beach.txt sea.txt');
      expect(page.url).toBe('/shelves/docs/files/Trip.album');
    });

    it('keep the listing as a preview of one of them opens', async () => {
      await renderAt('/shelves/docs/files/Trip.album?view=contents');
      await open('beach.txt');

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['beach.txt']));
      expect(row('sea.txt')).not.toBeNull();
    });
  });

  describe('its viewer', () => {
    it.each<[string, boolean, boolean]>([['readWrite', false, true], ['read', false, false], ['readWrite', true, false]])('is told whether the person may write: %s, on a replica %s, %s', async (permission, onReplica, canWrite) => {
      info = { permission, onReplica };
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      await vi.waitFor(() => expect(client.getQueryData(queryKeys.shelves.info('docs'))).toBeDefined());
      await act(async () => {});

      expect(given.canWrite).toBe(canWrite);
      expect([given.shelf, given.folder.path]).toEqual(['docs', '/Trip.album']);
    });

    it('lists its folder again once it has stored a file', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');

      await act(async () => void await given.contents.store('new.txt', new Blob(['new'])));

      await shows('beach.txt sea.txt new.txt');
      expect(sent.filter((line) => line.startsWith('PUT'))).toEqual(['PUT /api/shelves/docs/content?path=%2FTrip.album%2Fnew.txt&overwrite=true Bearer t']);
      expect(mounted).toBe(1);
    });

    it('may not write until that is known', async () => {
      infoHeld = new Promise(() => {});
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');

      expect(given.canWrite).toBe(false);
    });

    it('is drawn as its plugin\'s, as what reads that plugin\'s documents is to be', async () => {
      album(() => <p>of the plugin {usePluginId()}</p>);
      await renderAt('/shelves/docs/files/Trip.album');

      await shows('of the plugin a');
    });

    it('is given the same contents after a store', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      const { contents } = given;

      await act(async () => void await contents.store('new.txt', new Blob(['new'])));
      await shows('beach.txt sea.txt new.txt');

      expect(given.contents).toBe(contents);
    });

    describe('with files dropped on it', () => {
      let ownDrop: ((event: { preventDefault: () => void }) => void) | null;

      beforeEach(async () => {
        ownDrop = null;
        album(() => <div data-testid="album" onDragOver={(event) => ownDrop?.(event)}>beach<input type="file" /><input type="text" /></div>);
        await renderAt('/shelves/docs/files/Trip.album');
        await untilFound('[data-testid="album"]');
      });

      /** Drags over, and drops on, what `selector` finds; what is dragged is of the type `type`. Answers the events. */
      function drop(selector: string, type: string): DragEvent[] {
        return ['dragover', 'drop'].map((name) => {
          const event = new Event(name, { bubbles: true, cancelable: true }) as DragEvent;
          Object.defineProperty(event, 'dataTransfer', { value: { types: [type], dropEffect: 'copy' } });
          act(() => void document.querySelector(selector)!.dispatchEvent(event));
          return event;
        });
      }

      it('keeps the browser from opening them', () => {
        const events = drop('[data-testid="album"]', 'Files');

        expect(events.map(({ defaultPrevented, dataTransfer }) => [defaultPrevented, dataTransfer!.dropEffect])).toEqual([[true, 'none'], [true, 'none']]);
      });

      it('does so on the breadcrumbs too', () => {
        expect(drop('[aria-label="Pages of docs"]', 'Files').every(({ defaultPrevented }) => defaultPrevented)).toBe(true);
      });

      it('does so while it opens', async () => {
        page.to('/shelves/docs');
        await listed('/');
        folders.delete('/Trip.album');
        host.set({ plugins: [], awaited: true });
        page.to('/shelves/docs/files/Other.album');

        expect(drop('[aria-label="Pages of docs"]', 'Files').every(({ defaultPrevented }) => defaultPrevented)).toBe(true);
      });

      it.each([['a field for files', 'input[type="file"]', 'Files'], ['text over a field', 'input[type="text"]', 'text/plain']])('leaves to the browser what it can take: %s', (_, selector, type) => {
        expect(drop(selector, type).map(({ defaultPrevented }) => defaultPrevented)).toEqual([false, false]);
      });

      it('leaves to the viewer what it takes itself', () => {
        ownDrop = (event) => event.preventDefault();

        expect(drop('[data-testid="album"]', 'Files')[0].dataTransfer!.dropEffect).toBe('copy');
      });
    });

    it('opens another file in the preview of the first, which Back closes', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      const { open: openIt } = given.contents;

      act(() => openIt(shelfFile('beach.txt')));
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['beach.txt']));
      act(() => openIt(shelfFile('sea.txt')));
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['sea.txt']));
      page.back();

      await vi.waitFor(() => expect(dialogTitles()).toEqual([]));
      expect(page.url).toBe('/shelves/docs/files/Trip.album');
    });

    it('opens a file in the preview with its details and the plugins\' actions', async () => {
      const ran: string[] = [];
      host.set({ plugins: [{ ...plugin(Album), fileActions: [{ id: 'act', label: 'Act on it', showsFor: () => true, run: async ({ file }) => void ran.push(file.name) }] }] });
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      act(() => given.contents.open(shelfFile('beach.txt')));
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['beach.txt']));

      await act(async () => labelled('More actions')!.click());
      await press('Act on it');
      expect(ran).toEqual(['beach.txt']);

      await act(async () => labelled('Details')!.click());
      await shows('Details of beach.txt');
    });

    it('opens a file in the preview, alone', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');

      act(() => given.contents.open(shelfFile('beach.txt')));

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['beach.txt']));
      expect(page.url).toBe('/shelves/docs/files/Trip.album?preview=beach.txt');
      expect(labelled('Next file')).toBeNull();
      expect(viewer()).not.toBeNull();
    });

    it('opens a file nothing previews in a tab', async () => {
      const tabs = stubTabs();
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');

      act(() => given.contents.open(shelfFile('a.bin', { contentType: 'application/x-unknown' })));

      await vi.waitFor(() => expect(tabs.length).toBeGreaterThan(0));
      expect(dialogTitles()).toEqual([]);
    });

    it('does nothing more once it has gone', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      const { contents } = given;
      await chooseFrom('Pages of docs', 'Show contents');

      const before = sent.filter((line) => !line.startsWith('GET'));
      const file = shelfFile('beach.txt');

      const late = [contents.list(), contents.read(file).blob(), contents.store('late.txt', new Blob()), contents.move(file, 'late.txt'), contents.delete(file)];
      for (const each of late) await expect(each).rejects.toMatchObject({ name: 'AbortError' });
      act(() => contents.open(file));

      expect(dialogTitles()).toEqual([]);
      expect(sent.filter((line) => !line.startsWith('GET'))).toEqual(before);
    });

    it('has its contents where React mounts everything twice', async () => {
      await renderAt('/shelves/docs/files/Trip.album', { strict: true });

      await shows('beach.txt sea.txt');
      expect((await given.contents.list()).files).toHaveLength(2);
    });

    it('is another as another folder takes its place', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      folders.get('/Trip.album')!.id = 'another';
      const before = mounted;

      await refetch();

      await vi.waitFor(() => expect(given.folder.id).toBe('another'));
      expect(mounted).toBeGreaterThan(before);
    });

    it('gives way as its folder is gone', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      folders.delete('/Trip.album');

      await refetch();

      await shows('There is no directory.');
      expect(viewer()).toBeNull();
    });

    it('stays while its folder cannot be listed again', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('beach.txt sea.txt');
      listingFails = true;

      await refetch();
      await vi.waitFor(() => expect(client.getQueryState(queryKeys.shelfContents.listing('docs', '/Trip.album'))?.status).toBe('error'));
      await act(async () => {});

      expect(viewer()).not.toBeNull();
      expect(searchField()).toBeNull();
    });
  });

  describe('that is encrypted', () => {
    const PLAIN = new Blob(['the beach'], { type: 'text/plain' });

    beforeEach(async () => {
      check = await createCheck('right', FAST);
      const beach = shelfFile('beach.txt', { path: '/Trip.album/beach.txt', encrypted: true });
      folders.get('/Trip.album')!.files = [beach];
      stored.set(beach.id, await encryptBlob(PLAIN, 'right', 'text/plain', FAST));
    });

    it('asks for its password as it opens, and shows the viewer its decrypted files', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      expect([viewer(), row('beach.txt')]).toEqual([null, null]);
      await submitPassword('right');

      await shows('beach.txt');
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      expect(await (await given.contents.read(folders.get('/Trip.album')!.files[0]).blob()).text()).toBe('the beach');
    });

    it('opens with the password kept, asking for none', async () => {
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'right');
      await renderAt('/shelves/docs/files/Trip.album');

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      expect(dialogTitles()).toEqual([]);
    });

    it('asks again when the password kept no longer passes its check', async () => {
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'stale');
      await renderAt('/shelves/docs/files/Trip.album');

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      expect(viewer()).toBeNull();
    });

    it('says why its password could not be checked, and shows its listing', async () => {
      const said = vi.spyOn(toast, 'error').mockReturnValue('');
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'right');
      check = 'no check';
      await renderAt('/shelves/docs/files/Trip.album');

      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      expect(said).toHaveBeenCalledTimes(1);
      expect(button('Unlock')).not.toBeNull();
    });

    it('asks again as its password changes', async () => {
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'right');
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      check = await createCheck('another', FAST);

      await refetch();

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      expect(viewer()).toBeNull();
    });

    it('sends nothing the viewer stores once its encryption was removed, and shows it anew then', async () => {
      rememberPassword(folderPasswordKey('docs', 'dir:/Trip.album'), 'right');
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      const { contents } = given;
      const before = mounted;
      check = null;

      await expect(contents.store('new.txt', new Blob(['new']))).rejects.toThrow('Trip.album is no longer encrypted as it was, or where it was.');
      expect(sent.filter((line) => line.startsWith('PUT'))).toEqual([]);

      await refetch();
      await vi.waitFor(() => expect(given.contents).not.toBe(contents));
      expect(mounted).toBeGreaterThan(before);
      await act(async () => void await given.contents.store('new.txt', new Blob(['new'])));
      expect(sent.filter((line) => line.startsWith('PUT'))).toEqual(['PUT /api/shelves/docs/content?path=%2FTrip.album%2Fnew.txt&overwrite=true Bearer t']);
    });

    it('offers nothing in its page\'s menu while it waits for its password', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));

      await act(async () => labelled('Pages of docs')!.click());

      expect(button('Show contents')).toBeNull();
    });

    it('is asked for from a phone\'s top bar', async () => {
      await renderAt('/shelves/docs/files/Trip.album', { phone: true });
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      await press('Cancel');
      await vi.waitFor(() => expect(labelled('Unlock')).not.toBeNull());

      await act(async () => labelled('Unlock')!.click());

      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
    });

    it('asks nothing to show its contents, nor offers to unlock them', async () => {
      await renderAt('/shelves/docs/files/Trip.album?view=contents');

      await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      expect(dialogTitles()).toEqual([]);
      expect(button('Unlock')).toBeNull();
    });

    describe('with its prompt cancelled', () => {
      beforeEach(async () => {
        await renderAt('/shelves/docs/files/Trip.album');
        await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
        await press('Cancel');
        await vi.waitFor(() => expect(row('beach.txt')).not.toBeNull());
      });

      it('shows its listing, asking no more', async () => {
        await refetch();

        expect(dialogTitles()).toEqual([]);
        expect([searchField() !== null, viewer()]).toEqual([true, null]);
      });

      it('is asked for by Unlock, over the listing, and opens', async () => {
        await press('Unlock');
        await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
        expect(row('beach.txt')).not.toBeNull();
        await submitPassword('right');

        await vi.waitFor(() => expect(viewer()).not.toBeNull());
      });

      it('stays in its listing as that is cancelled too', async () => {
        await press('Unlock');
        await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
        await press('Cancel');

        await vi.waitFor(() => expect(button('Unlock')).not.toBeNull());
        expect([row('beach.txt') !== null, dialogTitles()]).toEqual([true, []]);
      });

      it('asks again as a folder of a type in it is opened', async () => {
        await open('Deep.ALBUM');

        await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
        expect(page.url).toBe('/shelves/docs/files/Trip.album/Deep.ALBUM');
      });

      it('asks again as it is opened again', async () => {
        await chooseFrom('Pages of docs', 'Files');
        await open('Trip.album');

        await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      });
    });

    it('takes its prompt back as it is left', async () => {
      await renderAt('/shelves/docs');
      await open('Trip.album');
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      page.back();

      await vi.waitFor(() => expect(row('reports')).not.toBeNull());
      expect(dialogTitles()).toEqual([]);
    });

    it('opens once its encryption is removed', async () => {
      await renderAt('/shelves/docs/files/Trip.album');
      await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock Trip.album']));
      check = null;

      await refetch();

      await vi.waitFor(() => expect(viewer()).not.toBeNull());
      expect(dialogTitles()).toEqual([]);
    });
  });

  describe('with a file that has a password of its own', () => {
    let own: ShelfFile;

    beforeEach(async () => {
      own = shelfFile('own.txt', { path: '/Trip.album/own.txt', encrypted: true });
      folders.get('/Trip.album')!.files = [own];
      stored.set(own.id, await encryptBlob(new Blob(['mine']), 'own', 'text/plain', FAST));
      await renderAt('/shelves/docs/files/Trip.album');
      await shows('own.txt');
    });

    it('reads it as locked, asking for nothing', async () => {
      await expect(given.contents.read(own).blob()).rejects.toMatchObject({ name: 'LockedError' });

      await act(async () => {});
      expect(dialogTitles()).toEqual([]);
    });

    it('reads it once the viewer has it unlocked', async () => {
      const reader = given.contents.read(own);
      const unlocking = reader.unlock();
      await vi.waitFor(async () => {
        await act(async () => {});
        expect(dialogTitles()).toEqual(['Unlock own.txt']);
      });
      await submitPassword('own');

      expect(await unlocking).toBe(true);
      expect(await (await reader.blob()).text()).toBe('mine');
    });
  });
});
