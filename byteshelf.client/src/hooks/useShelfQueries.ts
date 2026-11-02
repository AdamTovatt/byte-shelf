import { useCallback } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';
import {
  createDirectory,
  createShelf,
  deleteDirectory,
  deleteFile,
  deleteShelf,
  emptyTrash,
  fetchListing,
  fetchShelf,
  fetchShelves,
  fetchTrash,
  moveDirectory,
  moveFile,
  restoreDeletion,
  searchFiles,
  setDescription,
  updateShelf,
  type Entry,
} from '../services/shelfService';
import { joinPath, parentPath } from '../lib/shelfPaths';
import { getErrorMessage } from '../ui';

export function useShelves() {
  return useQuery({ queryKey: queryKeys.shelves.list, queryFn: fetchShelves });
}

export function useShelf(shelf: string) {
  return useQuery({ queryKey: queryKeys.shelves.info(shelf), queryFn: () => fetchShelf(shelf) });
}

/**
 * How long a folder's listing counts as fresh, and how long one no page shows is kept. Long, because
 * the live updates mark every listing of a shelf stale the moment it changes: until then a folder
 * seen before opens at once from the cache, and one marked stale still opens at once and refetches
 * quietly behind it. Only a folder never fetched waits on the network.
 */
const LISTING_STALE_TIME = 5 * 60_000;
const LISTING_GC_TIME = 30 * 60_000;

/** How many of a folder's subfolders are fetched ahead as it opens, so opening one of those is instant too. */
export const PREFETCHED_SUBFOLDERS = 20;

function listingQuery(shelf: string, path: string) {
  return {
    queryKey: queryKeys.shelfContents.listing(shelf, path),
    queryFn: () => fetchListing(shelf, path),
    staleTime: LISTING_STALE_TIME,
    gcTime: LISTING_GC_TIME,
  };
}

export function useListing(shelf: string, path: string) {
  return useQuery(listingQuery(shelf, path));
}

/** Fetches a folder's listing ahead of opening it; does nothing while a fresh one is cached. */
export function usePrefetchListing(shelf: string) {
  const queryClient = useQueryClient();
  return useCallback((path: string) => void queryClient.prefetchQuery(listingQuery(shelf, path)), [queryClient, shelf]);
}

/** The files under `under` whose name or description holds `text`; idle while `text` is blank. */
export function useFileSearch(shelf: string, under: string, text: string) {
  const trimmed = text.trim();
  return useQuery({
    queryKey: queryKeys.shelfContents.search(shelf, under, trimmed),
    queryFn: () => searchFiles(shelf, under, trimmed),
    enabled: trimmed !== '',
    placeholderData: keepPreviousData,
  });
}

export function useSetDescription(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: ({ id, description }: { id: string; description: string | null }) => setDescription(shelf, id, description),
    onSuccess: invalidate,
  });
}

export function useTrash(shelf: string) {
  return useQuery({ queryKey: queryKeys.shelfContents.trash(shelf), queryFn: () => fetchTrash(shelf) });
}

/** Refetches what a change on a shelf touches. The live updates do too, a moment later; this is for the tab that made it. */
export function useInvalidateShelf() {
  const queryClient = useQueryClient();
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.shelves.root });
    void queryClient.invalidateQueries({ queryKey: queryKeys.shelfContents.root });
  }, [queryClient]);
}

export function useCreateShelf() {
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: ({ name, quotaBytes }: { name: string; quotaBytes: number | null }) => createShelf(name, quotaBytes),
    onSuccess: invalidate,
  });
}

export function useUpdateShelf(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: ({ name, quotaBytes }: { name: string; quotaBytes: number | null }) => updateShelf(shelf, name, quotaBytes),
    onSuccess: invalidate,
  });
}

export function useDeleteShelf(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({ mutationFn: () => deleteShelf(shelf), onSuccess: invalidate });
}

export function useCreateDirectory(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({ mutationFn: (path: string) => createDirectory(shelf, path), onSuccess: invalidate });
}

interface Move {
  kind: Entry['kind'];
  id: string;
  toPath: string;
}

function moveEntry(shelf: string, { kind, id, toPath }: Move): Promise<unknown> {
  return kind === 'file' ? moveFile(shelf, id, toPath) : moveDirectory(shelf, id, toPath);
}

export function useMoveEntry(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({ mutationFn: (move: Move) => moveEntry(shelf, move), onSuccess: invalidate });
}

/**
 * A move refused: the name of the entry it stopped at and the server's reason, for a toast to set on
 * lines of their own. Its message, for a form, names the entry only when several were moving.
 */
export class MoveRefused extends Error {
  readonly entryName: string;
  readonly reason: string;

  constructor(entryName: string, caught: unknown, several: boolean) {
    const reason = getErrorMessage(caught, several ? 'it was refused' : 'Could not move it');
    super(several ? `Could not move ${entryName}: ${reason}` : reason);
    this.entryName = entryName;
    this.reason = reason;
  }
}

/**
 * Moves entries into the folder at `folder`, keeping their names, one at a time; those already there
 * are skipped. A refusal stops there and throws a MoveRefused; what moved before it stays moved. What
 * is shown is refetched once, after the last or the refusal, rather than once a move.
 */
export function useMoveEntries(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useCallback(async (entries: Entry[], folder: string): Promise<void> => {
    let moved = false;
    try {
      for (const entry of entries) {
        if (parentPath(entry.value.path) === folder) continue;
        try {
          await moveEntry(shelf, { kind: entry.kind, id: entry.value.id, toPath: joinPath(folder, entry.value.name) });
        } catch (caught) {
          throw new MoveRefused(entry.value.name, caught, entries.length > 1);
        }
        moved = true;
      }
    } finally {
      if (moved) invalidate();
    }
  }, [shelf, invalidate]);
}

export function useDeleteEntry(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: ({ kind, id }: { kind: 'file' | 'directory'; id: string }) =>
      kind === 'file' ? deleteFile(shelf, id) : deleteDirectory(shelf, id),
    onSuccess: invalidate,
  });
}

export function useRestoreDeletion(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({ mutationFn: (deletionId: string) => restoreDeletion(shelf, deletionId), onSuccess: invalidate });
}

export function useEmptyTrash(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({ mutationFn: () => emptyTrash(shelf), onSuccess: invalidate });
}
