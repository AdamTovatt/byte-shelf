import { useState } from 'react';
import toast from 'react-hot-toast';
import { Button, CardList, CardListItem, CopyableSecret, Dialog, Dropdown, DropdownItem, IconButton, Input, Spinner } from './ui';
import { Icon } from './ui/Icon';
import { useAuth } from '../contexts/AuthContext';
import { useCreatePairingCode, usePairWithPrimary, usePromote, useRemoveReplica, useReplicationStatus } from '../hooks/useReplicationQueries';
import { useSubmit } from '../hooks/useSubmit';
import { iconMap } from '../lib/iconMap';
import { formatTime } from '../lib/format';
import { getErrorMessage } from '../lib/utils';
import { describeReplica, describeSource } from '../lib/replication';
import type { PairingCodeResponse, ReplicaInfo, ReplicationSourceInfo } from '../services/replicationService';

/**
 * Replication, for admins: on a primary its replicas and how far behind each is, a pairing code for
 * another, or pairing this server with a primary; on a replica the primary it follows, and promoting it.
 * Its logic lives in lib/replication and is tested there; the rest is wiring that only a test against
 * two servers would exercise.
 */
export function ReplicationSection() {
  const { data: status, isLoading, error } = useReplicationStatus();

  return (
    <section className="space-y-3">
      <div className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">Replication</div>
      {isLoading && <Spinner />}
      {error && !status && <p className="text-body-sm text-danger-light dark:text-danger-dark">{getErrorMessage(error, 'Could not load the replication status')}</p>}
      {status && (status.source ? <ReplicaView source={status.source} /> : <PrimaryView replicas={status.replicas} />)}
    </section>
  );
}

/** A server that follows none is a primary, with or without replicas (Replication.md). */
function PrimaryView({ replicas }: { replicas: ReplicaInfo[] }) {
  const [code, setCode] = useState<PairingCodeResponse>();
  const [pairing, setPairing] = useState(false);
  const [removing, setRemoving] = useState<ReplicaInfo | null>(null);
  const createCode = useCreatePairingCode();
  const { error, submit } = useSubmit('Could not make a pairing code');

  return (
    <>
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">
        A replica keeps a whole copy of this server and follows it; it only reads.
      </p>
      {replicas.length > 0 && (
        <CardList>
          {replicas.map((replica) => (
            <CardListItem key={replica.id}>
              <div className="flex items-center gap-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-lg text-text-1-light dark:text-text-1-dark">{replica.name}</div>
                  <div className="truncate text-body-sm text-text-3-light dark:text-text-3-dark">{describeReplica(replica)}</div>
                </div>
                <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${replica.name}`} />}>
                  <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={() => setRemoving(replica)}>Remove</DropdownItem>
                </Dropdown>
              </div>
            </CardListItem>
          ))}
        </CardList>
      )}
      <div className="flex flex-wrap gap-3">
        <Button variant="secondary" loading={createCode.isPending} onClick={() => void submit(() => createCode.mutateAsync(), setCode)}>
          Make a pairing code
        </Button>
        {replicas.length === 0 && (
          <Button variant="secondary" onClick={() => setPairing(true)}>Follow a primary</Button>
        )}
      </div>
      {error && <p className="text-body-sm text-danger-light dark:text-danger-dark">{error}</p>}
      {code && <PairingCodeDialog code={code} onClose={() => setCode(undefined)} />}
      {pairing && <PairDialog onClose={() => setPairing(false)} />}
      {removing && <RemoveReplicaDialog replica={removing} onClose={() => setRemoving(null)} />}
    </>
  );
}

function PairingCodeDialog({ code, onClose }: { code: PairingCodeResponse; onClose: () => void }) {
  return (
    <Dialog open title="Pairing code" onClose={onClose}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">
        On the replica, paste it under Settings, Follow a primary. It works once, until {formatTime(code.expiresAt)}.
      </p>
      <CopyableSecret value={code.code} />
    </Dialog>
  );
}

function PairDialog({ onClose }: { onClose: () => void }) {
  const pair = usePairWithPrimary();
  const { logout } = useAuth();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const { error, submit } = useSubmit('Could not pair with the primary');

  function start(): Promise<void> {
    return submit(() => pair.mutateAsync({ code: code.trim(), name: name.trim() }), () => {
      // This server's users are about to be the primary's, this account included.
      toast.success('Paired. Sign in with your account on the primary once the copy is in.');
      void logout();
    });
  }

  return (
    <Dialog open title="Follow a primary" onClose={onClose} onSubmit={() => void start()} submitLabel="Pair" submitting={pair.isPending} error={error}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">
        This server becomes a read-only copy of the primary, its users included: the accounts here are replaced. It must have no shelves.
      </p>
      <Input label="Pairing code" value={code} onChange={(event) => setCode(event.target.value)} placeholder="Made in the primary's settings" autoComplete="off" required />
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="What the primary calls this server, such as backup" autoComplete="off" required />
    </Dialog>
  );
}

function RemoveReplicaDialog({ replica, onClose }: { replica: ReplicaInfo; onClose: () => void }) {
  const removeReplica = useRemoveReplica();
  const { error, submit } = useSubmit('Could not remove the replica');

  return (
    <Dialog
      open
      title={`Remove the replica ${replica.name}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removeReplica.mutateAsync(replica.id), onClose)}
      submitLabel="Remove"
      submitVariant="danger"
      submitting={removeReplica.isPending}
      error={error}
    >
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">It stops getting changes at once and keeps what it has. Pairing it again takes a new code and a server without shelves.</p>
    </Dialog>
  );
}

function ReplicaView({ source }: { source: ReplicationSourceInfo }) {
  const [promoting, setPromoting] = useState(false);

  return (
    <>
      <div className="flex items-center justify-between gap-4">
        <div className="min-w-0">
          <div className="truncate text-body-lg text-text-2-light dark:text-text-2-dark">
            A read-only replica of <span className="font-medium">{source.primaryUrl}</span>
          </div>
          <div className="text-body-sm text-text-3-light dark:text-text-3-dark">{describeSource(source)}</div>
          {source.lastError && (
            <div className="text-body-sm text-danger-light dark:text-danger-dark">
              {source.lastError}{source.lastErrorAt && ` (${formatTime(source.lastErrorAt)})`}
            </div>
          )}
        </div>
        <Button variant="outlineDanger" onClick={() => setPromoting(true)}>Promote</Button>
      </div>
      {promoting && <PromoteDialog primaryUrl={source.primaryUrl} onClose={() => setPromoting(false)} />}
    </>
  );
}

function PromoteDialog({ primaryUrl, onClose }: { primaryUrl: string; onClose: () => void }) {
  const promote = usePromote();
  const { error, submit } = useSubmit('Could not promote this server');

  function start(): Promise<void> {
    return submit(() => promote.mutateAsync(), (promoted) => {
      if (promoted.primaryTold) {
        toast.success('This server is now a primary');
      } else {
        toast.error('This server is now a primary, but the old one could not be told; remove it there');
      }

      onClose();
    });
  }

  return (
    <Dialog open title="Promote this server?" onClose={onClose} onSubmit={() => void start()} submitLabel="Promote" submitVariant="danger" submitting={promote.isPending} error={error}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">
        It stops following {primaryUrl} and takes changes itself; a copy in progress is dropped. Point clients and APP_BASE_URL here, and stop the old primary.
      </p>
    </Dialog>
  );
}
