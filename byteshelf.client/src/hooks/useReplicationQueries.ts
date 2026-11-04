import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../lib/queryKeys';
import {
  createPairingCode,
  fetchReplicationStatus,
  pairWithPrimary,
  promote,
  removeReplica,
  type PairWithPrimaryRequest,
} from '../services/replicationService';

/** How often the status is asked for while it is shown: lag and errors change without any event. */
const STATUS_POLL_MS = 5000;

export function useReplicationStatus() {
  return useQuery({ queryKey: queryKeys.replication.status, queryFn: fetchReplicationStatus, refetchInterval: STATUS_POLL_MS });
}

function useInvalidateStatus() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.replication.status });
  };
}

export function useCreatePairingCode() {
  return useMutation({ mutationFn: createPairingCode });
}

export function useRemoveReplica() {
  const invalidate = useInvalidateStatus();
  return useMutation({ mutationFn: (id: string) => removeReplica(id), onSuccess: invalidate });
}

export function usePairWithPrimary() {
  const invalidate = useInvalidateStatus();
  return useMutation({ mutationFn: (request: PairWithPrimaryRequest) => pairWithPrimary(request), onSuccess: invalidate });
}

export function usePromote() {
  const invalidate = useInvalidateStatus();
  return useMutation({ mutationFn: promote, onSuccess: invalidate });
}
