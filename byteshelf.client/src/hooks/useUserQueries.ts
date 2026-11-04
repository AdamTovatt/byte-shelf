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

export function useChangeOwnPassword() {
  return useMutation({ mutationFn: (request: UpdatePasswordRequest) => changeOwnPassword(request) });
}
