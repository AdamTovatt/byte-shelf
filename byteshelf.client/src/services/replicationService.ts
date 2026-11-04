import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import type { components } from '../types/api';

export type ReplicationStatus = components['schemas']['ReplicationStatus'];
export type ReplicaInfo = components['schemas']['ReplicaInfo'];
export type ReplicationSourceInfo = components['schemas']['ReplicationSourceInfo'];
export type PairingCodeResponse = components['schemas']['PairingCodeResponse'];
export type PairWithPrimaryRequest = components['schemas']['PairWithPrimaryRequest'];
export type PromoteResponse = components['schemas']['PromoteResponse'];

export function fetchReplicationStatus(): Promise<ReplicationStatus> {
  return unwrapResponse(apiClient.GET('/api/replication'));
}

export function createPairingCode(): Promise<PairingCodeResponse> {
  return unwrapResponse(apiClient.POST('/api/replication/codes'));
}

export function removeReplica(id: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/replication/replicas/{id}', { params: { path: { id } } }));
}

export function pairWithPrimary(request: PairWithPrimaryRequest): Promise<ReplicationStatus> {
  return unwrapResponse(apiClient.POST('/api/replication/pair', { body: request }));
}

export function promote(): Promise<PromoteResponse> {
  return unwrapResponse(apiClient.POST('/api/replication/promote'));
}
