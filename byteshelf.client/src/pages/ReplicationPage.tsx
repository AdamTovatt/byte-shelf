import { useState } from 'react';
import toast from 'react-hot-toast';
import { Button, Card, CopyField, Dropdown, DropdownItem, FormError, Icon, IconButton, Input, PageLayout, Spinner, iconMap } from '../ui';
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { SectionTitle } from '../components/SectionTitle';
import { CardList } from '../components/CardList';
import { useAuth } from '../contexts/AuthContext';
import { useCreatePairingCode, usePairWithPrimary, usePromote, useRemoveReplica, useReplicationStatus } from '../hooks/useReplicationQueries';
import { useSubmit } from '../hooks/useSubmit';
import { formatTime } from '../lib/format';
import { describeReplica, describeSource } from '../lib/replication';
import type { PairingCodeResponse, ReplicaInfo, ReplicationSourceInfo } from '../services/replicationService';

/**
 * Replication, a server-wide setting for admins: on a primary its replicas and how far behind each is,
 * a pairing code for another, or pairing this server with a primary; on a replica the primary it
 * follows, and promoting it. Its logic lives in lib/replication and is tested there; the rest is
 * wiring that only a test against two servers would exercise.
 */
export function ReplicationPage() {
  const { data: status, isLoading, error } = useReplicationStatus();

  return (
    <PageLayout className="space-y-6">
      <div className="space-y-1">
        <h1 className="text-heading-md text-fg">Replication</h1>
        <p className="text-body-md text-muted">A replica keeps a whole copy of this server and follows it; it only reads.</p>
      </div>
      {isLoading && <Spinner />}
      {!status && <LoadError error={error} failure="Could not load the replication status" />}
      {status && (status.source ? <ReplicaView source={status.source} /> : <PrimaryView replicas={status.replicas} />)}
    </PageLayout>
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
      <section className="space-y-4">
        <SectionTitle
          actions={
            <Button variant="main" size="sm" loading={createCode.isPending} onClick={() => void submit(() => createCode.mutateAsync(), setCode)}>
              Make a pairing code
            </Button>
          }
        >
          Replicas
        </SectionTitle>
        <FormError message={error} />
        {replicas.length === 0 ? (
          <p className="text-body-md text-muted">No replicas yet. Make a pairing code here, and paste it on the new server under Replication.</p>
        ) : (
          <CardList>
            {replicas.map((replica) => (
              <div key={replica.id} className="flex items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-md text-fg">{replica.name}</div>
                  <div className="truncate text-body-sm text-muted">{describeReplica(replica)}</div>
                </div>
                <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${replica.name}`} />}>
                  <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={() => setRemoving(replica)}>Remove</DropdownItem>
                </Dropdown>
              </div>
            ))}
          </CardList>
        )}
      </section>

      {replicas.length === 0 && (
        <section className="space-y-4">
          <SectionTitle
            description="Make this server a read-only copy of another one. It must have no shelves."
            actions={<Button variant="secondary" size="sm" onClick={() => setPairing(true)}>Follow a primary</Button>}
          >
            Make this server a replica
          </SectionTitle>
        </section>
      )}

      {code && <PairingCodeDialog code={code} onClose={() => setCode(undefined)} />}
      {pairing && <PairDialog onClose={() => setPairing(false)} />}
      {removing && <RemoveReplicaDialog replica={removing} onClose={() => setRemoving(null)} />}
    </>
  );
}

function PairingCodeDialog({ code, onClose }: { code: PairingCodeResponse; onClose: () => void }) {
  return (
    <FormDialog open title="Pairing code" onClose={onClose}>
      <p className="text-body-md text-fg">
        On the new server, paste it under Replication, Follow a primary. It works once, until {formatTime(code.expiresAt)}.
      </p>
      <CopyField label="Pairing code" value={code.code} copyButtonAriaLabel="Copy the pairing code" />
    </FormDialog>
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
    <FormDialog open title="Follow a primary" onClose={onClose} onSubmit={() => void start()} submitLabel="Pair" submitting={pair.isPending} error={error}>
      <p className="text-body-md text-fg">
        This server becomes a read-only copy of the primary, its users included: the accounts here are replaced. It must have no shelves.
      </p>
      <Input label="Pairing code" value={code} onChange={(event) => setCode(event.target.value)} placeholder="Made on the primary, under Replication" autoComplete="off" required />
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="What the primary calls this server, such as backup" autoComplete="off" required />
    </FormDialog>
  );
}

function RemoveReplicaDialog({ replica, onClose }: { replica: ReplicaInfo; onClose: () => void }) {
  const removeReplica = useRemoveReplica();
  const { error, submit } = useSubmit('Could not remove the replica');

  return (
    <FormDialog
      open
      title={`Remove the replica ${replica.name}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removeReplica.mutateAsync(replica.id), onClose)}
      submitLabel="Remove"
      submitVariant="danger"
      submitting={removeReplica.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">It stops getting changes at once and keeps what it has. Pairing it again takes a new code and a server without shelves.</p>
    </FormDialog>
  );
}

function ReplicaView({ source }: { source: ReplicationSourceInfo }) {
  const [promoting, setPromoting] = useState(false);

  return (
    <section className="space-y-4">
      <SectionTitle actions={<Button variant="outlineDanger" size="sm" onClick={() => setPromoting(true)}>Promote</Button>}>
        This server is a replica
      </SectionTitle>
      <Card variant="elevated" padding="none" className="px-4 py-3 space-y-1">
        <div className="truncate text-body-md text-fg">
          A read-only replica of <span className="font-medium">{source.primaryUrl}</span>
        </div>
        <div className="text-body-sm text-muted">{describeSource(source)}</div>
        {source.lastError && (
          <div className="text-body-sm text-danger-11">
            {source.lastError}{source.lastErrorAt && ` (${formatTime(source.lastErrorAt)})`}
          </div>
        )}
      </Card>
      {promoting && <PromoteDialog primaryUrl={source.primaryUrl} onClose={() => setPromoting(false)} />}
    </section>
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
    <FormDialog open title="Promote this server?" onClose={onClose} onSubmit={() => void start()} submitLabel="Promote" submitVariant="danger" submitting={promote.isPending} error={error}>
      <p className="text-body-md text-fg">
        It stops following {primaryUrl} and takes changes itself; a copy in progress is dropped. Point clients and APP_BASE_URL here, and stop the old primary.
      </p>
    </FormDialog>
  );
}
