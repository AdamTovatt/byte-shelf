import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
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

export function useSetGrant(shelf: string) {
  const invalidate = useInvalidateAccess();
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: string; permission: ShelfAccessPermission }) => setGrant(shelf, userId, permission),
    onSuccess: invalidate,
  });
}

export function useRemoveGrant(shelf: string) {
  const invalidate = useInvalidateAccess();
  return useMutation({ mutationFn: (userId: string) => removeGrant(shelf, userId), onSuccess: invalidate });
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
