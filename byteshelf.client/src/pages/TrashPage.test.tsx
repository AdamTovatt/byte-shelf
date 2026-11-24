// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost } from '../contexts/DialogHost';
import { TrashPage } from './TrashPage';
import { queryKeys } from '../lib/queryKeys';
import { saveTokens } from '../services/authService';
import type { DeletionTree, ShelfFile, TrashEntry } from '../services/shelfService';
import { encryptBlob } from '../lib/encryption/age';
import { dialogTitles, labelled, shows, submitPassword } from '../test/dom';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The files saved from a blob here, by name. */
const saved = vi.hoisted(() => [] as string[]);

vi.mock('../lib/localBlobs', async (original) => ({
  ...(await original<typeof import('../lib/localBlobs')>()),
  showBlob: () => true,
  saveBlob: (_blob: Blob, name: string) => {
    saved.push(name);
  },
  saveFrom: () => {},
}));

// The header's crumbs and views are its own tests'; here it only needs to stay out of the way.
vi.mock('../components/ShelfHeader', () => ({ ShelfHeader: () => null }));

const deletedFile: TrashEntry = {
  deletionId: 'del-file', kind: 'file', entryId: 'file-1', path: '/a.txt', deletedAt: '2026-11-02T00:00:00Z', purgesAt: '2026-12-02T00:00:00Z', fileCount: 1, sizeBytes: 5,
};

const deletedFolder: TrashEntry = {
  deletionId: 'del-folder', kind: 'directory', entryId: 'dir-1', path: '/photos', deletedAt: '2026-11-02T00:00:00Z', purgesAt: '2026-12-02T00:00:00Z', fileCount: 2, sizeBytes: 9,
};

const file: ShelfFile = {
  id: 'file-1', shelfId: 's', directoryId: 'root', path: '/a.txt', name: 'a.txt', contentType: 'text/plain', sizeBytes: 5, sha256: 'x',
  createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z', description: null, encrypted: false,
};

const fileDeletion: DeletionTree = {
  deletion: deletedFile,
  tree: { directory: { id: 'root', shelfId: 's', parentId: null, path: '/', name: '', createdAt: '2026-11-01T00:00:00Z', encrypted: false }, directories: [], files: [file] },
};

describe('the trash page', () => {
  let root: Root;
  let container: HTMLDivElement;
  let sent: string[];
  let search: string;
  let goBack: () => void;
  let goTo: (url: string) => void;
  let restored: boolean;
  /** The file the deletion took, its stored contents and the encrypted folder it was in. */
  let taken: ShelfFile;
  let stored: Blob;
  let encryptedBy: DeletionTree['encryptedBy'];
  let client: QueryClient;

  function Location() {
    search = useLocation().search;
    const navigate = useNavigate();
    goBack = () => act(() => navigate(-1));
    goTo = (url) => act(() => navigate(url));
    return null;
  }

  beforeEach(async () => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    saved.length = 0;
    restored = false;
    taken = file;
    stored = new Blob(['hello'], { type: 'text/plain' });
    encryptedBy = undefined;
    sessionStorage.clear();
    // Plain HTTP, where a file is read here and saved from a blob.
    vi.stubGlobal('isSecureContext', false);
    sent = stubServer(async (request) => {
      const { pathname } = new URL(request.url);
      if (pathname === '/api/shelves/docs/trash') return json([deletedFile, deletedFolder]);
      if (restored && pathname.includes('/del-file/')) return json({ message: 'There is no such deletion.' }, 404);
      if (pathname === '/api/shelves/docs/trash/del-file/tree') return json({ ...fileDeletion, encryptedBy, tree: { ...fileDeletion.tree, files: [taken] } });
      if (pathname === '/api/shelves/docs/trash/del-folder/tree') return json({ ...fileDeletion, deletion: deletedFolder });
      if (pathname === '/api/shelves/docs/trash/del-file/content') return new Response(stored);
      return json({ message: 'Not here.' }, 404);
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    await renderAt('/shelves/docs/trash');
    await vi.waitFor(() => expect(container.textContent).toContain('/photos'));
  });

  async function renderAt(url: string): Promise<void> {
    root = createRoot(container);
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[url]}>
          <DialogHost><Routes><Route path="/shelves/:shelf/trash" element={<TrashPage />} /></Routes></DialogHost>
          <Location />
        </MemoryRouter>
      </QueryClientProvider>,
    ));
  }

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('offers Download on a deleted file only, and says on a deleted folder where it downloads whole', () => {
    expect(container.querySelector('button[aria-label="Download /a.txt"]')).not.toBeNull();
    expect(container.querySelector('button[aria-label="Download /photos"]')).toBeNull();
    expect(container.querySelectorAll('[role="img"][aria-label*="command line or an agent"]')).toHaveLength(1);
  });

  it('saves a deleted file read from its deletion, leaving it in the trash', async () => {
    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Download /a.txt"]')!.click());

    await vi.waitFor(() => expect(saved).toEqual(['a.txt']));
    expect(sent).toContain('GET /api/shelves/docs/trash/del-file/content?id=file-1 Bearer first');
    expect(sent.filter((request) => request.startsWith('POST'))).toEqual([]);
  });

  function dialog(): Element | null {
    return document.querySelector('[role="dialog"]');
  }

  it.each([
    ['a click on its path', () => Array.from(container.querySelectorAll('button')).find((each) => each.textContent === '/a.txt')],
    ['Open', () => container.querySelector<HTMLButtonElement>('button[aria-label="Open /a.txt"]')],
  ])('previews a deleted file on %s, read from its deletion, named in the URL, until Back', async (_by, row) => {
    await act(async () => row()!.click());

    await shows('hello');
    expect(search).toBe('?preview=del-file');
    expect(sent).toContain('GET /api/shelves/docs/trash/del-file/content?id=file-1 Bearer first');
    expect([labelled('Download'), labelled('Open in new tab')]).not.toContain(null);
    expect([labelled('Details'), labelled('Next file')]).toEqual([null, null]);
    await act(async () => labelled('Download')!.click());
    expect(saved).toEqual(['a.txt']);
    goBack();
    expect([search, dialog()]).toEqual(['', null]);
  });

  it('offers no preview of a deleted folder, nor of a file in one', async () => {
    const said = vi.spyOn(toast, 'error').mockReturnValue('');
    expect(container.querySelector('button[aria-label="Open /photos"]')).toBeNull();
    act(() => root.unmount());

    await renderAt('/shelves/docs/trash?preview=del-folder');

    await vi.waitFor(() => expect(said).toHaveBeenCalledWith('That file is no longer here.', expect.anything()));
    await vi.waitFor(() => expect([search, dialog()]).toEqual(['', null]));
  });

  it('keeps the preview on a reload, and closes it with a toast once the file is restored', async () => {
    const said = vi.spyOn(toast, 'error').mockReturnValue('');
    act(() => root.unmount());
    await renderAt('/shelves/docs/trash?preview=del-file');
    await shows('hello');

    restored = true;
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));

    await vi.waitFor(() => expect(said).toHaveBeenCalledWith('That file is no longer here.', expect.anything()));
    expect([search, dialog()]).toEqual(['', null]);
  });

  it('previews a file that was gone once it is back', async () => {
    vi.spyOn(toast, 'error').mockReturnValue('');
    const openIt = () => act(async () => labelled('Open /a.txt')!.click());
    await openIt();
    await shows('hello');
    restored = true;
    await act(() => client.invalidateQueries({ queryKey: queryKeys.shelfContents.root }));
    await vi.waitFor(() => expect(dialog()).toBeNull());

    restored = false;
    await openIt();

    await shows('hello');
    expect(toast.error).toHaveBeenCalledTimes(1);
  });

  it('asks for the password of a file deleted from an encrypted folder as it opens, and shows it read from its deletion', async () => {
    taken = { ...file, encrypted: true, path: '/vault/a.txt' };
    stored = await encryptBlob(new Blob(['the secret']), 'pw', 'text/plain', 10);
    encryptedBy = { directory: { ...fileDeletion.tree.directory, id: 'vault', path: '/vault', name: 'vault', encrypted: true }, check: '' };

    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Open /a.txt"]')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('Unlock vault'));
    await submitPassword('pw');

    await shows('the secret');
    expect(sent.filter((request) => request.includes('/content') || request.includes('/encryption'))).toEqual(
      sent.filter((request) => request.startsWith('GET /api/shelves/docs/trash/del-file/content?id=file-1 ')),
    );
  });

  it('takes its prompt back as another shelf\'s trash is opened', async () => {
    taken = { ...file, encrypted: true, path: '/vault/a.txt' };
    encryptedBy = { directory: { ...fileDeletion.tree.directory, id: 'vault', path: '/vault', name: 'vault', encrypted: true }, check: '' };
    await act(async () => labelled('Download /a.txt')!.click());
    await vi.waitFor(() => expect(dialogTitles()).toEqual(['Unlock vault']));

    goTo('/shelves/other/trash');

    await vi.waitFor(() => expect(dialogTitles()).toEqual([]));
  });

  it('does not ask again for a deletion that is not there', async () => {
    vi.spyOn(toast, 'error').mockReturnValue('');
    restored = true;
    act(() => root.unmount());

    await renderAt('/shelves/docs/trash?preview=del-file');

    await vi.waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(sent.filter((request) => request.includes('/del-file/tree'))).toHaveLength(1);
  });

  it('says there is no preview of a deleted file of a type no viewer takes', async () => {
    taken = { ...file, contentType: 'application/zip' };

    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="Open /a.txt"]')!.click());

    await shows('There is no preview of files of this type.');
  });
});
