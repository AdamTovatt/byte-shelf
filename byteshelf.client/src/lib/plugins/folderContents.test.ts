// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { folderContents, type ClosableFolderContents, type FolderAccess } from './folderContents';
import type { ContentSource } from '../../services/contentService';
import type { StoreOptions } from '../../services/fileStoring';
import { saveTokens } from '../../services/authService';
import { queryKeys } from '../queryKeys';
import { installFakeWebLocks } from '../../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../../test/fakeServer';
import { shelfFile } from '../../test/shelfFiles';

const trip = { id: 'trip', shelfId: 's', parentId: null, path: '/Trip.album', name: 'Trip.album', createdAt: '2026-11-02T00:00:00Z', encrypted: false };
const beach = shelfFile('beach.jpg', { path: '/Trip.album/beach.jpg', encrypted: true });

describe('a folder\'s contents, as its viewer has them', () => {
  let sent: string[];
  let bodies: unknown[];
  /** The folder the shelf has at the path, whose contents it answers. */
  let found: typeof trip;
  let contents: ClosableFolderContents;
  let store: ReturnType<typeof vi.fn<FolderAccess['store']>>;
  let read: ReturnType<typeof vi.fn<FolderAccess['reading']['read']>>;
  let changed: ReturnType<typeof vi.fn<() => void>>;
  let open: ReturnType<typeof vi.fn<FolderAccess['open']>>;

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    found = trip;
    bodies = [];
    sent = stubServer(async (request) => {
      request.signal.throwIfAborted();
      const url = new URL(request.url);
      if (url.pathname.endsWith('/tree')) return json({ directory: found, directories: [], files: [beach], nextCursor: url.searchParams.get('after') ? null : 'next' });
      if (url.searchParams.get('id') === 'gone') return json({ message: 'There is no such file.', code: 'not_found' }, 404);
      if (request.method === 'DELETE') return json({ deletionId: 'x' });
      const body = await request.json() as { toPath: string };
      bodies.push(body);
      return json({ ...beach, path: body.toPath });
    });
    store = vi.fn<FolderAccess['store']>(async (_, path) => shelfFile('stored', { path }));
    read = vi.fn<FolderAccess['reading']['read']>(async () => null);
    changed = vi.fn<() => void>();
    open = vi.fn<FolderAccess['open']>();
    contents = folderContents({
      shelf: 'docs', folder: { directory: trip, encryptedBy: null }, store, changed, open,
      reading: { source: {} as ContentSource, read, unlock: async () => null },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Reads that stay on their way until they are aborted. */
  function keepReading(): void {
    read.mockImplementation((_, signal) => new Promise((_resolve, reject) => signal!.addEventListener('abort', () => reject(signal!.reason as Error))));
  }

  it('lists everything below it, a page at a time', async () => {
    expect(await contents.list()).toMatchObject({ files: [beach], nextCursor: 'next' });
    expect(await contents.list('next')).toMatchObject({ nextCursor: null });

    expect(sent).toEqual(['GET /api/shelves/docs/tree?under=%2FTrip.album Bearer t', 'GET /api/shelves/docs/tree?under=%2FTrip.album&after=next Bearer t']);
  });

  it('ends a listing as its signal aborts', async () => {
    const aborting = new AbortController();
    const listing = contents.list(undefined, aborting.signal);
    aborting.abort();

    await expect(listing).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('has a query of it asked again as anything on the shelf changes', () => {
    expect(contents.queryKey.slice(0, queryKeys.shelfContents.root.length)).toEqual(queryKeys.shelfContents.root);
  });

  it('stores a file by its path in the folder', async () => {
    const whole = new Blob(['new']);

    expect((await contents.store('small/beach.jpg', whole)).path).toBe('/Trip.album/small/beach.jpg');

    expect(store.mock.calls[0].slice(0, 3)).toEqual([{ directory: trip, encryptedBy: null }, '/Trip.album/small/beach.jpg', whole]);
  });

  it('moves a file by its path in the folder', async () => {
    const moved = await contents.move(beach, 'small/b.jpg');

    expect(moved.path).toBe('/Trip.album/small/b.jpg');
    expect(sent.at(-1)).toBe('POST /api/shelves/docs/files/move?id=beach.jpg Bearer t');
    expect(bodies).toEqual([{ toPath: '/Trip.album/small/b.jpg' }]);
  });

  it('moves nothing once the folder has been moved away', async () => {
    found = { ...trip, id: 'another' };

    await expect(contents.move(beach, 'b.jpg')).rejects.toThrow('Trip.album is no longer where it was.');

    expect(bodies).toEqual([]);
  });

  it('deletes a file', async () => {
    await contents.delete(beach);

    expect(sent).toEqual(['DELETE /api/shelves/docs/files?id=beach.jpg Bearer t']);
  });

  it.each<[string, () => Promise<unknown>]>([
    ['stored', () => contents.store('a.txt', new Blob())],
    ['moved', () => contents.move(beach, 'b.jpg')],
    ['deleted', () => contents.delete(beach)],
  ])('has what is shown refetched once a file is %s', async (_, change) => {
    const changing = change();
    expect(changed).not.toHaveBeenCalled();
    await changing;

    expect(changed).toHaveBeenCalledOnce();
  });

  it('has nothing refetched for what is refused', async () => {
    await expect(contents.delete(shelfFile('gone'))).rejects.toThrow('There is no such file.');

    expect(changed).not.toHaveBeenCalled();
  });

  it.each<[string, () => Promise<unknown>]>([
    ['store over', () => contents.store('', new Blob())],
    ['move to', () => contents.move(beach, '')],
  ])('takes no empty path, the folder\'s own, to %s', async (_, change) => {
    await expect(change()).rejects.toThrow('A file in the folder has a name.');

    expect(store).not.toHaveBeenCalled();
    expect(sent).toEqual([]);
  });

  it('fails as given up when the password to store with is not given', async () => {
    store.mockResolvedValue(null);

    await expect(contents.store('a.txt', new Blob())).rejects.toMatchObject({ name: 'AbortError' });
    expect(changed).not.toHaveBeenCalled();
  });

  /** A store that, as the real one, gives up what it asks for, and goes on until it is ended or finished. */
  function keepStoring(): { finish: () => void } {
    let finish: () => void = () => undefined;
    store.mockImplementation((_, path, _contents, { asking, storing }: StoreOptions) => new Promise((resolve, reject) => {
      finish = () => resolve(asking?.aborted && path.endsWith('locked.txt') ? null : shelfFile('stored', { path }));
      storing?.addEventListener('abort', () => reject(storing.reason as Error));
    }));
    return { finish: () => finish() };
  }

  it('ends a store as its signal aborts', async () => {
    keepStoring();
    const aborting = new AbortController();
    const storing = contents.store('a.txt', new Blob(), aborting.signal);
    aborting.abort();

    await expect(storing).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('goes on storing as it is closed', async () => {
    const { finish } = keepStoring();
    const storing = contents.store('a.txt', new Blob());
    contents.close();
    finish();

    expect((await storing).path).toBe('/Trip.album/a.txt');
    expect(changed).toHaveBeenCalledOnce();
  });

  it('asks for a password to store with no longer as it is closed', async () => {
    const { finish } = keepStoring();
    const storing = contents.store('locked.txt', new Blob());
    contents.close();
    finish();

    await expect(storing).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('ends what is read as it is closed', async () => {
    keepReading();
    const reading = contents.read(beach).blob();
    contents.close();

    await expect(reading).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('ends what is listed as it is closed', async () => {
    const listing = contents.list();
    contents.close();

    await expect(listing).rejects.toMatchObject({ name: 'AbortError' });
  });

  it.each<[string, () => Promise<unknown>]>([
    ['list', () => contents.list()],
    ['read', () => contents.read(beach).blob()],
    ['store', () => contents.store('a.txt', new Blob())],
    ['move', () => contents.move(beach, 'b.jpg')],
    ['delete', () => contents.delete(beach)],
  ])('does not %s after it is closed', async (_, use) => {
    contents.close();

    await expect(use()).rejects.toMatchObject({ name: 'AbortError' });
    expect([read.mock.calls, store.mock.calls, sent.filter((line) => !line.startsWith('GET'))]).toEqual([[], [], []]);
  });

  it('lets go of what one reader read as that is closed, alone', async () => {
    read.mockResolvedValue(new Blob(['plain']));
    const [one, other] = [contents.read(beach), contents.read(beach)];
    await one.blob();

    one.close();

    await expect(one.blob()).rejects.toMatchObject({ name: 'AbortError' });
    expect(await (await other.blob()).text()).toBe('plain');
  });

  it('opens a file as the listing does', () => {
    contents.open(beach);

    expect(open).toHaveBeenCalledWith(beach);
  });

  it('opens nothing after it is closed', () => {
    contents.close();
    contents.open(beach);

    expect(open).not.toHaveBeenCalled();
  });
});
