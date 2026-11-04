// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TrashPage } from './TrashPage';
import { saveTokens } from '../services/authService';
import type { DeletionTree, ShelfFile, TrashEntry } from '../services/shelfService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The files saved from a blob here, by name. */
const saved = vi.hoisted(() => [] as string[]);

vi.mock('../lib/localBlobs', () => ({
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

  beforeEach(async () => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    saved.length = 0;
    // Plain HTTP, where a file is read here and saved from a blob.
    vi.stubGlobal('isSecureContext', false);
    sent = stubServer(async (request) => {
      const { pathname } = new URL(request.url);
      if (pathname === '/api/shelves/docs/trash') return json([deletedFile, deletedFolder]);
      if (pathname === '/api/shelves/docs/trash/del-file/tree') return json(fileDeletion);
      if (pathname === '/api/shelves/docs/trash/del-file/content') return new Response('hello', { headers: { 'Content-Type': 'text/plain' } });
      return json({ message: 'Not here.' }, 404);
    });
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={['/shelves/docs/trash']}>
          <Routes><Route path="/shelves/:shelf/trash" element={<TrashPage />} /></Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    ));
    await vi.waitFor(() => expect(container.textContent).toContain('/photos'));
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
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
});
