import { useCallback } from 'react';
import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { QUERY_RETRIES } from '../lib/queryClient';
import { queryKeys } from '../lib/queryKeys';
import type { SearchCriteria } from '../lib/searchFilters';
import {
  createDirectory,
  createShelf,
  deleteDirectory,
  deleteFile,
  deleteShelf,
  emptyTrash,
  fetchDeletion,
  fetchFile,
  fetchFileMetadata,
  fetchListing,
  fetchShelf,
  fetchShelves,
  fetchTrash,
  findDuplicates,
  moveDirectory,
  moveFile,
  restoreDeletion,
  searchFiles,
  setDescription,
  updateShelf,
  type Entry,
  type ShelfFile,
} from '../services/shelfService';
import { isNotFound } from '../services/apiClient';
import { joinPath, parentPath } from '../lib/shelfPaths';
import { getErrorMessage } from '../ui';

export function useShelves() {
  return useQuery(shelvesQuery);
}

const shelvesQuery = { queryKey: queryKeys.shelves.list, queryFn: fetchShelves };

/** The name the shelf `id` has now. */
export async function shelfName(queryClient: QueryClient, id: string): Promise<string> {
  const shelves = await queryClient.fetchQuery(shelvesQuery);
  const shelf = shelves.find((each) => each.id === id);
  if (!shelf) throw new Error('The file is on no shelf you reach.');
  return shelf.name;
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

/**
 * A search's pages, fetched one at a time: `fetchNextPage` adds the page after the last one's
 * `nextCursor`. The previous search's pages stay shown while a new one loads. Idle while `enabled` is false.
 */
function usePagedSearch<Page extends { nextCursor?: string | null }>(
  queryKey: readonly unknown[],
  fetchPage: (after: string | undefined) => Promise<Page>,
  enabled: boolean,
) {
  return useInfiniteQuery({
    queryKey,
    queryFn: ({ pageParam }) => fetchPage(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** The files under `under` that meet `criteria`, a page at a time. Idle while `enabled` is false. */
export function useFileSearch(shelf: string, under: string, criteria: SearchCriteria, enabled: boolean) {
  return usePagedSearch(
    queryKeys.shelfContents.search(shelf, under, criteria),
    (after) => searchFiles(shelf, under, criteria, after),
    enabled,
  );
}

/**
 * The groups of files under `under` with the same contents, among those that meet `criteria`, a page
 * of groups at a time. Idle while `enabled` is false.
 */
export function useDuplicateSearch(shelf: string, under: string, criteria: SearchCriteria, enabled: boolean) {
  return usePagedSearch(
    queryKeys.shelfContents.duplicates(shelf, under, criteria),
    (after) => findDuplicates(shelf, under, criteria, after),
    enabled,
  );
}

/**
 * A live file's record, refetched as the shelf changes, so a file moved elsewhere is still found and
 * one that is gone fails with the server's 404. `listed`, the record a list on the page already holds,
 * stands in until the first answer; one fetched before is not kept to stand in instead, which the list
 * may be newer than: of a file encrypted since, it would show the encrypted bytes. Idle while `id` is null.
 */
export function useFile(shelf: string, id: string | null, listed?: ShelfFile) {
  return useQuery({
    queryKey: queryKeys.shelfContents.file(shelf, id ?? ''),
    queryFn: () => fetchFile(shelf, id!),
    enabled: id !== null,
    placeholderData: listed,
    gcTime: 0,
    retry: unlessNotThere,
  });
}

/** What a deletion took, refetched as the shelf changes; one restored or purged fails with the server's 404. Idle while `deletionId` is null. */
export function useDeletion(shelf: string, deletionId: string | null) {
  return useQuery({
    queryKey: queryKeys.shelfContents.deletion(shelf, deletionId ?? ''),
    queryFn: () => fetchDeletion(shelf, deletionId!),
    enabled: deletionId !== null,
    retry: unlessNotThere,
  });
}

/** Asks again, as for any query, unless the server said it is not there. */
function unlessNotThere(failures: number, error: Error): boolean {
  return failures < QUERY_RETRIES && !isNotFound(error);
}

/**
 * What the server read from a file's contents and what plugins keep about it, fetched afresh each time it is
 * asked for: what is read is not in the live updates, which would refetch every open listing for each file
 * read. What plugins keep about the file itself is.
 */
export function useFileMetadata(shelf: string, id: string) {
  return useQuery({
    queryKey: queryKeys.shelfContents.metadata(shelf, id),
    queryFn: () => fetchFileMetadata(shelf, id),
    staleTime: 0,
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
