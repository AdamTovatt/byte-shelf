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
} from '../services/shelfService';

export function useShelves() {
  return useQuery({ queryKey: queryKeys.shelves.list, queryFn: fetchShelves });
}

export function useShelf(shelf: string) {
  return useQuery({ queryKey: queryKeys.shelves.info(shelf), queryFn: () => fetchShelf(shelf) });
}

export function useListing(shelf: string, path: string) {
  return useQuery({ queryKey: queryKeys.shelfContents.listing(shelf, path), queryFn: () => fetchListing(shelf, path) });
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
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.shelves.root });
    void queryClient.invalidateQueries({ queryKey: queryKeys.shelfContents.root });
  };
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

export function useMoveEntry(shelf: string) {
  const invalidate = useInvalidateShelf();
  return useMutation({
    mutationFn: ({ kind, id, toPath }: { kind: 'file' | 'directory'; id: string; toPath: string }) =>
      kind === 'file' ? moveFile(shelf, id, toPath) : moveDirectory(shelf, id, toPath),
    onSuccess: invalidate,
  });
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
