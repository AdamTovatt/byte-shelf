import { useState, type FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, ButtonGroup, CardList, CardListItem, CopyableSecret, Dialog, IconButton, Input, SectionTitle } from '../components/ui';
import { Icon } from '../components/ui/Icon';
import { ShelfHeader } from '../components/ShelfHeader';
import { useCreateKey, useDeleteKey, useGrants, useKeys, useRemoveGrant, useSetGrant } from '../hooks/useAccessQueries';
import { useDeleteShelf, useShelf, useUpdateShelf } from '../hooks/useShelfQueries';
import { useUsers } from '../hooks/useUserQueries';
import { iconMap } from '../lib/iconMap';
import { formatQuotaMegabytes, formatTime, parseQuotaMegabytes } from '../lib/format';
import { shelfViewRoute } from '../lib/shelfPaths';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import type { CreatedShelfKey, ShelfAccessGrant, ShelfAccessPermission, ShelfKeyInfo, ShelfKeyPermission } from '../services/accessService';
import type { ShelfInfo } from '../services/shelfService';

const ACCESS_OPTIONS: { value: ShelfAccessPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'readWrite', label: 'Read and write' },
];

const KEY_OPTIONS: { value: ShelfKeyPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'addOnly', label: 'Add only' },
  { value: 'readWrite', label: 'Read and write' },
];

/** An admin's view of one shelf: its name and quota, who may use it, its keys, and deleting it. */
export function ShelfSettingsPage() {
  const shelf = useParams().shelf!;

  return (
    <div className="p-6 md:p-8">
      <div className="max-w-3xl mx-auto space-y-10">
        <ShelfHeader shelf={shelf} view="settings" />
        <SettingsSection shelf={shelf} />
        <GrantsSection shelf={shelf} />
        <KeysSection shelf={shelf} />
        <DeleteSection shelf={shelf} />
      </div>
    </div>
  );
}

function SettingsSection({ shelf }: { shelf: string }) {
  const { data: info } = useShelf(shelf);
  // Keyed on what the server holds, so a change made elsewhere starts the form afresh.
  return info ? <SettingsForm key={`${info.name}:${info.quotaBytes}`} info={info} /> : null;
}

function SettingsForm({ info }: { info: ShelfInfo }) {
  const shelf = info.name;
  const updateShelf = useUpdateShelf(shelf);
  const navigate = useNavigate();
  const [name, setName] = useState(info.name);
  const [quota, setQuota] = useState(formatQuotaMegabytes(info.quotaBytes));
  const { error, submit } = useSubmit('Could not save');

  function save(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    return submit(() => updateShelf.mutateAsync({ name: name.trim(), quotaBytes: parseQuotaMegabytes(quota) }), (updated) => {
      toast.success('Saved');
      if (updated.name !== shelf) navigate(shelfViewRoute(updated.name, 'settings'), { replace: true });
    });
  }

  return (
    <section className="space-y-3">
      <SectionTitle>Name and quota</SectionTitle>
      <form onSubmit={(event) => void save(event)} className="space-y-3">
        <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" required />
        <Input label="Quota in MiB" value={quota} onChange={(event) => setQuota(event.target.value)} placeholder="No limit" inputMode="decimal" autoComplete="off" error={error} />
        <p className="text-body-sm text-text-4-light dark:text-text-4-dark">A new name changes the shelf's links. A quota below what it holds only stops new uploads.</p>
        <Button type="submit" variant="primary" loading={updateShelf.isPending}>Save</Button>
      </form>
    </section>
  );
}

function GrantsSection({ shelf }: { shelf: string }) {
  const { data: grants = [] } = useGrants(shelf);
  const setGrant = useSetGrant(shelf);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<ShelfAccessGrant | null>(null);

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <SectionTitle>Access</SectionTitle>
        <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.add} size={16} />} onClick={() => setAdding(true)}>Give access</Button>
      </div>
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Admins reach every shelf; everyone else only the ones they are given.</p>
      {grants.length > 0 && (
        <CardList>
          {grants.map((grant) => (
            <CardListItem key={grant.userId}>
              <div className="flex flex-wrap items-center gap-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-lg text-text-1-light dark:text-text-1-dark">{grant.displayName}</div>
                  <div className="truncate text-body-sm text-text-3-light dark:text-text-3-dark">{grant.email}</div>
                </div>
                <ButtonGroup
                  size="sm"
                  options={ACCESS_OPTIONS}
                  value={grant.permission}
                  onChange={(permission) => void runOrToast(() => setGrant.mutateAsync({ userId: grant.userId, permission }), 'Could not change the access')}
                />
                <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Take ${grant.email}'s access away`} onClick={() => setRemoving(grant)} />
              </div>
            </CardListItem>
          ))}
        </CardList>
      )}
      {adding && <GiveAccessDialog shelf={shelf} grantedUserIds={grants.map((grant) => grant.userId)} onClose={() => setAdding(false)} />}
      {removing && <RemoveGrantDialog shelf={shelf} grant={removing} onClose={() => setRemoving(null)} />}
    </section>
  );
}

function RemoveGrantDialog({ shelf, grant, onClose }: { shelf: string; grant: ShelfAccessGrant; onClose: () => void }) {
  const removeGrant = useRemoveGrant(shelf);
  const { error, submit } = useSubmit('Could not take the access away');

  return (
    <Dialog
      open
      title={`Take ${grant.email}'s access away?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removeGrant.mutateAsync(grant.userId), onClose)}
      submitLabel="Take away"
      submitVariant="danger"
      submitting={removeGrant.isPending}
      error={error}
    >
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">They no longer see {shelf}. What they stored stays on it.</p>
    </Dialog>
  );
}

function GiveAccessDialog({ shelf, grantedUserIds, onClose }: { shelf: string; grantedUserIds: string[]; onClose: () => void }) {
  const { data: users = [] } = useUsers();
  const setGrant = useSetGrant(shelf);
  const [email, setEmail] = useState('');
  const [permission, setPermission] = useState<ShelfAccessPermission>('read');
  const { error, setError, submit } = useSubmit('Could not give access');

  function give(): Promise<void> {
    const user = users.find((candidate) => candidate.email.toLowerCase() === email.trim().toLowerCase());
    if (!user) {
      setError(`There is no user with the email ${email.trim()}`);
      return Promise.resolve();
    }

    if (grantedUserIds.includes(user.id)) {
      setError(`${user.email} has access already; change it in the list`);
      return Promise.resolve();
    }

    return submit(() => setGrant.mutateAsync({ userId: user.id, permission }), onClose);
  }

  return (
    <Dialog open title="Give access" onClose={onClose} onSubmit={() => void give()} submitLabel="Give access" submitting={setGrant.isPending} error={error}>
      <Input label="User's email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="off" required />
      <ButtonGroup options={ACCESS_OPTIONS} value={permission} onChange={setPermission} />
    </Dialog>
  );
}

function KeysSection({ shelf }: { shelf: string }) {
  const { data: keys = [] } = useKeys(shelf);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<ShelfKeyInfo | null>(null);

  return (
    <section className="space-y-3">
      <div className="flex items-center justify-between">
        <SectionTitle>Keys</SectionTitle>
        <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.key} size={16} />} onClick={() => setCreating(true)}>New key</Button>
      </div>
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">A key lets a program use this shelf, and only this shelf. Deleting it stops it at once.</p>
      {keys.length > 0 && (
        <CardList>
          {keys.map((key) => (
            <CardListItem key={key.id}>
              <div className="flex items-center gap-3">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-lg text-text-1-light dark:text-text-1-dark">{key.name}</div>
                  <div className="text-body-sm text-text-3-light dark:text-text-3-dark">
                    {KEY_OPTIONS.find((option) => option.value === key.permission)?.label} · created {formatTime(key.createdAt)}
                  </div>
                </div>
                <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Delete the key ${key.name}`} onClick={() => setDeleting(key)} />
              </div>
            </CardListItem>
          ))}
        </CardList>
      )}
      {creating && <CreateKeyDialog shelf={shelf} onClose={() => setCreating(false)} />}
      {deleting && <DeleteKeyDialog shelf={shelf} shelfKey={deleting} onClose={() => setDeleting(null)} />}
    </section>
  );
}

function DeleteKeyDialog({ shelf, shelfKey, onClose }: { shelf: string; shelfKey: ShelfKeyInfo; onClose: () => void }) {
  const deleteKey = useDeleteKey(shelf);
  const { error, submit } = useSubmit('Could not delete the key');

  return (
    <Dialog
      open
      title={`Delete the key ${shelfKey.name}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => deleteKey.mutateAsync(shelfKey.id), onClose)}
      submitLabel="Delete"
      submitVariant="danger"
      submitting={deleteKey.isPending}
      error={error}
    >
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">Whatever uses it stops working at once. It cannot be undone.</p>
    </Dialog>
  );
}

function CreateKeyDialog({ shelf, onClose }: { shelf: string; onClose: () => void }) {
  const createKey = useCreateKey(shelf);
  const [name, setName] = useState('');
  const [permission, setPermission] = useState<ShelfKeyPermission>('addOnly');
  const [created, setCreated] = useState<CreatedShelfKey>();
  const { error, submit } = useSubmit('Could not create the key');

  function create(): Promise<void> {
    return submit(() => createKey.mutateAsync({ name: name.trim(), permission }), setCreated);
  }

  if (created) {
    return (
      <Dialog open title={`Key "${created.key.name}"`} onClose={onClose}>
        <p className="text-body-md text-text-2-light dark:text-text-2-dark">Copy the secret now: it is shown this once.</p>
        <CopyableSecret value={created.secret} />
      </Dialog>
    );
  }

  return (
    <Dialog open title="New key" onClose={onClose} onSubmit={() => void create()} submitLabel="Create" submitting={createKey.isPending} error={error}>
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="What uses it, such as backup job" autoComplete="off" required />
      <ButtonGroup options={KEY_OPTIONS} value={permission} onChange={setPermission} />
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Add only stores new files and never replaces or deletes one.</p>
    </Dialog>
  );
}

function DeleteSection({ shelf }: { shelf: string }) {
  const deleteShelf = useDeleteShelf(shelf);
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const { error, setError, submit } = useSubmit('Could not delete the shelf');

  function remove(): Promise<void> {
    return submit(() => deleteShelf.mutateAsync(), () => {
      toast.success(`Deleted ${shelf}`);
      navigate('/', { replace: true });
    });
  }

  return (
    <section className="space-y-3">
      <SectionTitle>Delete the shelf</SectionTitle>
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Only an empty shelf can be deleted: delete what it holds and empty its trash first.</p>
      <Button variant="outlineDanger" onClick={() => setConfirming(true)}>Delete shelf</Button>
      <Dialog
        open={confirming}
        title={`Delete ${shelf}?`}
        onClose={() => {
          setConfirming(false);
          setError(undefined);
        }}
        onSubmit={() => void remove()}
        submitLabel="Delete"
        submitVariant="danger"
        submitting={deleteShelf.isPending}
        error={error}
      >
        <p className="text-body-md text-text-2-light dark:text-text-2-dark">Its access grants and keys are deleted with it.</p>
      </Dialog>
    </section>
  );
}
