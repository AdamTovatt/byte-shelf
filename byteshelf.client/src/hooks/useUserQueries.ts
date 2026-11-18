import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';
import {
  changeOwnPassword,
  createUser,
  deleteUser,
  fetchAllUsers,
  fetchCurrentUser,
  isAdmin,
  setRoles,
  type CreateUserRequest,
  type UpdatePasswordRequest,
} from '../services/userService';
import { addPasskey, fetchPasskeys, removePasskey, renamePasskey } from '../services/passkeyService';

export function useCurrentUser() {
  return useQuery({ queryKey: queryKeys.users.me, queryFn: fetchCurrentUser });
}

/** Whether the signed-in user is an admin; false until that is known. */
export function useIsAdmin(): boolean {
  return isAdmin(useCurrentUser().data);
}

export function useUsers() {
  return useQuery({ queryKey: queryKeys.users.list, queryFn: fetchAllUsers });
}

function useInvalidateUsers() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.users.root });
  };
}

export function useCreateUser() {
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: (request: CreateUserRequest) => createUser(request), onSuccess: invalidate });
}

export function useSetRoles() {
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: ({ userId, roles }: { userId: string; roles: string[] }) => setRoles(userId, roles), onSuccess: invalidate });
}

export function useDeleteUser() {
  const invalidate = useInvalidateUsers();
  return useMutation({ mutationFn: (userId: string) => deleteUser(userId), onSuccess: invalidate });
}

export function usePasskeys() {
  return useQuery({ queryKey: queryKeys.users.passkeys, queryFn: fetchPasskeys });
}

function useInvalidatePasskeys() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.users.passkeys });
  };
}

export function useAddPasskey() {
  const invalidate = useInvalidatePasskeys();
  return useMutation({ mutationFn: (currentPassword: string) => addPasskey(currentPassword), onSuccess: invalidate });
}

export function useRenamePasskey() {
  const invalidate = useInvalidatePasskeys();
  return useMutation({ mutationFn: ({ passkeyId, name }: { passkeyId: string; name: string }) => renamePasskey(passkeyId, name), onSuccess: invalidate });
}

export function useRemovePasskey() {
  const invalidate = useInvalidatePasskeys();
  return useMutation({ mutationFn: (passkeyId: string) => removePasskey(passkeyId), onSuccess: invalidate });
}

export function useChangeOwnPassword() {
  return useMutation({ mutationFn: (request: UpdatePasswordRequest) => changeOwnPassword(request) });
}
