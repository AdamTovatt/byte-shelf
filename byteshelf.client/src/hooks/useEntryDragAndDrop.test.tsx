// @vitest-environment happy-dom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import type { DragEndEvent, DragStartEvent } from '@dnd-kit/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useEntryDragAndDrop } from './useEntryDragAndDrop';
import { folderDropId } from '../lib/dropTargets';
import type { Entry } from '../services/shelfService';
import { textOf } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The moves the server was asked for, and the names it refuses to move. */
const server = vi.hoisted(() => ({ moves: [] as string[], refusing: new Set<string>() }));

vi.mock('../services/shelfService', async (importOriginal) => {
  const move = async (_shelf: string, id: string, toPath: string) => {
    const name = toPath.slice(toPath.lastIndexOf('/') + 1);
    if (server.refusing.has(name)) throw new Error('The folder is encrypted.');
    server.moves.push(`${id} to ${toPath}`);
    return {};
  };
  return { ...(await importOriginal<typeof import('../services/shelfService')>()), moveFile: move, moveDirectory: move };
});

function folder(name: string): Entry {
  return {
    kind: 'directory',
    value: { id: name, shelfId: 's', parentId: null, path: `/${name}`, name, createdAt: '', encrypted: false },
    sizeBytes: 0,
    fileCount: 0,
    encryptedFileCount: 0,
  };
}

function file(name: string): Entry {
  return {
    kind: 'file',
    value: { id: name, shelfId: 's', directoryId: 'root', path: `/${name}`, name, contentType: 'text/plain', sizeBytes: 1, sha256: '', createdAt: '', updatedAt: '', encrypted: false },
  };
}

const reports = folder('reports');
const archive = folder('archive');
const a = file('a.txt');
const b = file('b.txt');
const c = file('c.txt');
const ROWS = [reports, archive, a, b, c];

describe('moving rows by dragging them', () => {
  let root: Root;
  let drag: ReturnType<typeof useEntryDragAndDrop>;
  let refetches: number;
  let errors: string[];

  function Probe({ selected }: { selected: Entry[] }) {
    drag = useEntryDragAndDrop({ shelf: 'docs', entries: ROWS, selected });
    return null;
  }

  async function render(selected: Entry[]): Promise<void> {
    const client = new QueryClient();
    vi.spyOn(client, 'invalidateQueries').mockImplementation(async (filters) => {
      if (filters?.queryKey?.[0] === 'shelfContents') refetches++;
    });
    await act(async () => root.render(<QueryClientProvider client={client}><Probe selected={selected} /></QueryClientProvider>));
  }

  /** Drags the row `id` and drops it on the folder at `path`, or on nothing, then waits for the moves. */
  async function dragOnto(id: string, path: string | null): Promise<void> {
    act(() => drag.onDragStart({ active: { id } } as DragStartEvent));
    act(() => drag.onDragEnd({ over: path === null ? null : { id: folderDropId(path) } } as DragEndEvent));
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)));
  }

  beforeEach(() => {
    server.moves = [];
    server.refusing.clear();
    refetches = 0;
    errors = [];
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(textOf(message as ReactNode));
      return '';
    });
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.restoreAllMocks();
  });

  it('moves a row outside the selection by itself', async () => {
    await render([a, b]);

    await dragOnto('c.txt', '/reports');

    expect(server.moves).toEqual(['c.txt to /reports/c.txt']);
  });

  it('moves the whole selection when one of its rows is dragged, refetching once for all of them', async () => {
    await render([a, b, c]);

    await dragOnto('b.txt', '/reports');

    expect(server.moves).toEqual(['a.txt to /reports/a.txt', 'b.txt to /reports/b.txt', 'c.txt to /reports/c.txt']);
    expect(refetches).toBe(1);
  });

  it('never offers a folder being dragged as a place to drop it', async () => {
    await render([reports, a]);

    act(() => drag.onDragStart({ active: { id: 'a.txt' } } as DragStartEvent));

    expect([drag.takesDrop(reports), drag.takesDrop(archive), drag.takesDrop(c)]).toEqual([false, true, false]);
  });

  it('skips what is already in the folder it is dropped on', async () => {
    await render([a, b]);

    await dragOnto('a.txt', '/');

    expect([server.moves, refetches, errors]).toEqual([[], 0, []]);
  });

  it('stops at the first refusal and names it, keeping what moved before it', async () => {
    server.refusing.add('b.txt');
    await render([a, b, c]);

    await dragOnto('a.txt', '/archive');

    expect(server.moves).toEqual(['a.txt to /archive/a.txt']);
    expect(errors).toEqual(['Could not move b.txt:' + 'The folder is encrypted.']);
    expect(refetches).toBe(1);
  });

  it('moves nothing when dropped outside any folder', async () => {
    await render([a]);

    await dragOnto('a.txt', null);

    expect([server.moves, drag.dragged]).toEqual([[], null]);
  });
});
