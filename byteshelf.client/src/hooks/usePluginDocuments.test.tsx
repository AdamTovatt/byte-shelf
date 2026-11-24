// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { usePluginDocuments, useSetPluginDocument, type PluginDocumentWrite } from './usePluginDocuments';
import { invalidateResource } from '../lib/invalidate';
import { queryClient } from '../lib/queryClient';
import { queryKeys } from '../lib/queryKeys';
import { saveTokens } from '../services/authService';
import type { FileMetadata, PluginFileMetadata, ShelfFile } from '../services/shelfService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const FILE = { id: 'f', shelfId: 's' } as ShelfFile;

const NONE = { contents: null, file: null, fileVersion: null };

function kept(fileDocument: Record<string, unknown> | null, fileVersion: number, contentsDocument: Record<string, unknown> | null = null): PluginFileMetadata {
  return { name: 'Notes', contentsDocument, fileDocument, fileVersion, fields: [] };
}

describe('a plugin\'s documents about a file', () => {
  let root: Root;
  let read: ReturnType<typeof usePluginDocuments>;
  let set: ReturnType<typeof useSetPluginDocument>;
  let plugins: Record<string, PluginFileMetadata>;
  let refusal: Response | null;
  let holdReads: Promise<void> | null;
  let file: ShelfFile;
  let answerShelves: () => Promise<Response>;
  let sent: string[];
  let bodies: unknown[];

  function Probe() {
    read = usePluginDocuments('notes', file);
    set = useSetPluginDocument('notes', file);
    return null;
  }

  function render() {
    return act(async () => root.render(<QueryClientProvider client={queryClient}><Probe /></QueryClientProvider>));
  }

  function write(change: PluginDocumentWrite) {
    return act(async () => set.mutateAsync(change));
  }

  beforeEach(() => {
    localStorage.clear();
    queryClient.clear();
    queryClient.setDefaultOptions({ queries: { ...queryClient.getDefaultOptions().queries, retry: false } });
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    plugins = {};
    refusal = null;
    file = FILE;
    answerShelves = async () => json([{ id: 'other', name: 'other' }, { id: 's', name: 'docs' }]);
    holdReads = null;
    bodies = [];
    sent = stubServer(async (request) => {
      if (new URL(request.url).pathname === '/api/shelves') return answerShelves();
      if (request.method === 'PUT') {
        bodies.push(await request.json());
        return refusal ?? json(plugins.notes);
      }
      await holdReads;
      return json({ readable: true, readAt: null, plugins });
    });
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('are the plugin\'s own, read from the shelf the file is on', async () => {
    plugins = { notes: kept({ rating: 4 }, 3, { words: 'apple' }), other: kept({ rating: 1 }, 1) };
    await render();

    await vi.waitFor(() => expect(read.data).toEqual({ contents: { words: 'apple' }, file: { rating: 4 }, fileVersion: 3 }));
    expect(sent.map((request) => request.split(' ')[1])).toEqual(['/api/shelves', '/api/shelves/docs/files/metadata?id=f']);
  });

  it('are none when the plugin keeps nothing about it', async () => {
    plugins = { other: kept({ rating: 1 }, 1) };
    await render();

    await vi.waitFor(() => expect(read.data).toEqual(NONE));
  });

  it('keep the version of one that was removed, to write over', async () => {
    plugins = { notes: kept(null, 2) };
    await render();

    await vi.waitFor(() => expect(read.data).toEqual({ ...NONE, fileVersion: 2 }));
  });

  it('are read again when another tab changes one', async () => {
    plugins = { notes: kept({ rating: 4 }, 1) };
    await render();
    await vi.waitFor(() => expect(read.data?.fileVersion).toBe(1));

    plugins = { notes: kept({ rating: 5 }, 2) };
    act(() => invalidateResource('shelf-contents'));

    await vi.waitFor(() => expect(read.data).toEqual({ ...NONE, file: { rating: 5 }, fileVersion: 2 }));
  });

  it('are written over the version named, and what was written is there before it is read again', async () => {
    plugins = { notes: kept({ rating: 4 }, 1), other: kept({ rating: 1 }, 1) };
    await render();
    await vi.waitFor(() => expect(read.data?.fileVersion).toBe(1));
    let release = () => {};
    holdReads = new Promise((resolve) => { release = resolve; });

    plugins = { ...plugins, notes: kept({ rating: 5 }, 2) };
    await expect(write({ document: { rating: 5 }, expectedVersion: 1 })).resolves.toEqual({ ...NONE, file: { rating: 5 }, fileVersion: 2 });

    expect(bodies).toEqual([{ document: { rating: 5 }, expectedVersion: 1 }]);
    expect(sent.filter((request) => request.startsWith('PUT')).map((request) => request.split(' ')[1])).toEqual(['/api/shelves/docs/files/plugin-metadata?id=f&plugin=notes']);
    await vi.waitFor(() => expect(read.data).toEqual({ ...NONE, file: { rating: 5 }, fileVersion: 2 }));
    expect(Object.keys(queryClient.getQueryData<FileMetadata>(queryKeys.shelfContents.metadata('s', 'f'))!.plugins!)).toEqual(['notes', 'other']);
    release();
  });

  it('are removed by writing null', async () => {
    plugins = { notes: kept({ rating: 4 }, 1) };
    await render();
    await vi.waitFor(() => expect(read.data?.fileVersion).toBe(1));

    plugins = { notes: kept(null, 2) };
    await write({ document: null, expectedVersion: 1 });

    expect(bodies).toEqual([{ document: null, expectedVersion: 1 }]);
    await vi.waitFor(() => expect(read.data).toEqual({ ...NONE, fileVersion: 2 }));
  });

  it('are read again when a write is refused, to write over what is there', async () => {
    plugins = { notes: kept({ rating: 4 }, 1) };
    await render();
    await vi.waitFor(() => expect(read.data?.fileVersion).toBe(1));

    plugins = { notes: kept({ rating: 2 }, 2) };
    refusal = json({ message: 'It was changed.' }, 409);
    await expect(write({ document: { rating: 5 }, expectedVersion: 1 })).rejects.toMatchObject({ status: 409 });

    await vi.waitFor(() => expect(read.data).toEqual({ ...NONE, file: { rating: 2 }, fileVersion: 2 }));
  });

  it('have no contents once the file is encrypted, whatever was read before', async () => {
    plugins = { notes: kept({ rating: 4 }, 1, { words: 'apple' }) };
    await render();
    await vi.waitFor(() => expect(read.data?.contents).toEqual({ words: 'apple' }));

    file = { ...FILE, encrypted: true };
    holdReads = new Promise(() => {});
    await render();

    expect(read.data).toEqual({ ...NONE, file: { rating: 4 }, fileVersion: 1 });
  });

  it('are written for the first time over no version', async () => {
    await render();
    await vi.waitFor(() => expect(read.data).toEqual(NONE));

    plugins = { notes: kept({ rating: 5 }, 1) };
    await write({ document: { rating: 5 }, expectedVersion: null });

    expect(bodies).toEqual([{ document: { rating: 5 }, expectedVersion: null }]);
  });

  it('are written before any are read, leaving what is read whole', async () => {
    let release = () => {};
    holdReads = new Promise((resolve) => { release = resolve; });
    await render();

    plugins = { notes: kept({ rating: 5 }, 1), other: kept({ rating: 1 }, 1) };
    await write({ document: { rating: 5 }, expectedVersion: null });

    expect(queryClient.getQueryData(queryKeys.shelfContents.metadata('s', 'f'))).toBeUndefined();
    release();
    await vi.waitFor(() => expect(read.data?.fileVersion).toBe(1));
  });

  it('are written once the shelf is known', async () => {
    let answer!: (response: Response) => void;
    answerShelves = () => new Promise((resolve) => { answer = resolve; });
    await render();

    plugins = { notes: kept({ rating: 5 }, 1) };
    const written = act(async () => set.mutateAsync({ document: { rating: 5 }, expectedVersion: null }));
    await vi.waitFor(() => expect(sent.length).toBe(1));
    expect(bodies).toEqual([]);
    answer(json([{ id: 's', name: 'docs' }]));

    await expect(written).resolves.toMatchObject({ fileVersion: 1 });
  });

  it.each([
    ['the shelves cannot be listed', () => json({ message: 'The disk is gone.' }, 500), 'The disk is gone.'],
    ['the file is on no shelf there is', () => json([{ id: 'other', name: 'other' }]), 'The file is on no shelf you reach.'],
  ])('fail, read or written, when %s', async (_, answer, message) => {
    answerShelves = async () => answer();
    await render();

    await vi.waitFor(() => expect(read.error?.message).toBe(message));
    await expect(write({ document: {}, expectedVersion: null })).rejects.toThrow(message);
    expect(bodies).toEqual([]);
  });
});
