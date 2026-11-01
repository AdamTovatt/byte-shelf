import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';
import {
  createKey,
  deleteKey,
  fetchGrants,
  fetchKeys,
  removeGrant,
  setGrant,
  type ShelfAccessPermission,
  type ShelfKeyPermission,
} from '../services/accessService';

export function useGrants(shelf: string) {
  return useQuery({ queryKey: queryKeys.shelfAccess.grants(shelf), queryFn: () => fetchGrants(shelf) });
}

export function useKeys(shelf: string) {
  return useQuery({ queryKey: queryKeys.shelfAccess.keys(shelf), queryFn: () => fetchKeys(shelf) });
}

function useInvalidateAccess() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.shelfAccess.root });
  };
}

/**
 * Every grant on each of `shelves`, by shelf: what a user's row on the users page reads. One request a
 * shelf, the same queries each shelf's settings use, so the event stream keeps both fresh. `error` is
 * the first shelf's whose grants could not be loaded, which then read as none.
 */
export function useGrantsByShelf(shelves: string[]) {
  return useQueries({
    queries: shelves.map((shelf) => ({ queryKey: queryKeys.shelfAccess.grants(shelf), queryFn: () => fetchGrants(shelf) })),
    combine: (results) => ({
      byShelf: new Map(shelves.map((shelf, index) => [shelf, results[index].data ?? []])),
      error: results.find((result) => result.error)?.error ?? null,
    }),
  });
}

/** Gives a user access to a shelf, or changes it. */
export function useSetGrant() {
  const invalidate = useInvalidateAccess();
  return useMutation({
    mutationFn: ({ shelf, userId, permission }: { shelf: string; userId: string; permission: ShelfAccessPermission }) => setGrant(shelf, userId, permission),
    onSuccess: invalidate,
  });
}

/** Takes a user's access to a shelf away. */
export function useRemoveGrant() {
  const invalidate = useInvalidateAccess();
  return useMutation({ mutationFn: ({ shelf, userId }: { shelf: string; userId: string }) => removeGrant(shelf, userId), onSuccess: invalidate });
}

export function useCreateKey(shelf: string) {
  const invalidate = useInvalidateAccess();
  return useMutation({
    mutationFn: ({ name, permission }: { name: string; permission: ShelfKeyPermission }) => createKey(shelf, name, permission),
    onSuccess: invalidate,
  });
}

export function useDeleteKey(shelf: string) {
  const invalidate = useInvalidateAccess();
  return useMutation({ mutationFn: (keyId: string) => deleteKey(shelf, keyId), onSuccess: invalidate });
}
