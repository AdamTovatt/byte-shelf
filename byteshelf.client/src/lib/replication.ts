import { formatTime } from './format';
import type { ReplicaInfo, ReplicationSourceInfo } from '../services/replicationService';

function changes(count: number): string {
  return `${count} ${count === 1 ? 'change' : 'changes'}`;
}

/** A replica's lag in one line, as its primary last heard: "3 changes behind · seen 12 Mar 2026, 10:04". */
export function describeReplica(replica: ReplicaInfo): string {
  const lag = replica.changesBehind === 0 ? 'Up to date' : `${changes(replica.changesBehind)} behind`;
  const seen = replica.lastSeenAt ? `seen ${formatTime(replica.lastSeenAt)}` : 'not seen yet';
  return `${lag} · ${seen}`;
}

/** Where a replica stands with its primary in one line: copying, or how far it has applied and when. */
export function describeSource(source: ReplicationSourceInfo): string {
  if (source.copying) {
    return 'Taking a whole copy';
  }

  const synced = source.lastSyncedAt ? `last synced ${formatTime(source.lastSyncedAt)}` : 'not synced yet';
  return `Applied through change ${source.appliedSequence} · ${synced}`;
}
