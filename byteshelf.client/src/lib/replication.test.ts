import { describe, expect, it } from 'vitest';
import { describeReplica, describeSource } from './replication';
import type { ReplicaInfo, ReplicationSourceInfo } from '../services/replicationService';

const replica: ReplicaInfo = { id: 'r', name: 'backup', pairedAt: '2026-03-01T10:00:00Z', appliedSequence: 7, changesBehind: 0 };
const source: ReplicationSourceInfo = { primaryUrl: 'https://shelf.example.com', pairedAt: '2026-03-01T10:00:00Z', appliedSequence: 42, copying: false };

describe('describeReplica', () => {
  it('says how far behind a replica is, and whether it has been seen', () => {
    expect(describeReplica(replica)).toBe('Up to date · not seen yet');
    expect(describeReplica({ ...replica, changesBehind: 1 })).toBe('1 change behind · not seen yet');
    expect(describeReplica({ ...replica, changesBehind: 3, lastSeenAt: '2026-03-01T10:00:00Z' })).toMatch(/^3 changes behind · seen /);
  });
});

describe('describeSource', () => {
  it('says a replica is copying, or how far it has applied the log', () => {
    expect(describeSource({ ...source, copying: true })).toBe('Taking a whole copy');
    expect(describeSource(source)).toBe('Applied through change 42 · not synced yet');
    expect(describeSource({ ...source, lastSyncedAt: '2026-03-01T10:00:00Z' })).toMatch(/^Applied through change 42 · last synced /);
  });
});
