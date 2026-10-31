import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, CardList, Dialog, Input, ListButton, ProgressBar, Spinner } from '../components/ui';
import { useCreateShelf, useShelves } from '../hooks/useShelfQueries';
import { useIsAdmin } from '../hooks/useUserQueries';
import { iconMap } from '../lib/iconMap';
import { describeUsage, parseQuotaMegabytes, usedShare } from '../lib/format';
import { shelfRoute } from '../lib/shelfPaths';
import { useSubmit } from '../hooks/useSubmit';
import { Icon } from '../components/ui/Icon';
import type { ShelfInfo } from '../services/shelfService';

/** The shelves the user reaches, with their usage; admins create new ones here. */
export function ShelvesPage() {
  const { data: shelves, isLoading } = useShelves();
  const isAdmin = useIsAdmin();
  const [creating, setCreating] = useState(false);

  return (
    <div className="p-6 md:p-8">
      <div className="max-w-3xl mx-auto space-y-6">
        <div className="flex items-center justify-between gap-4">
          <h1 className="text-heading-md text-text-1-light dark:text-text-1-dark">Shelves</h1>
          {isAdmin && (
            <Button variant="primary" icon={<Icon icon={iconMap.add} size={18} />} onClick={() => setCreating(true)}>
              New shelf
            </Button>
          )}
        </div>

        {isLoading && <Spinner />}
        {shelves && shelves.length === 0 && (
          <p className="text-body-lg text-text-3-light dark:text-text-3-dark">
            {isAdmin ? 'No shelves yet.' : 'You have not been given access to any shelf yet.'}
          </p>
        )}
        {shelves && shelves.length > 0 && (
          <CardList>
            {shelves.map((shelf) => <ShelfRow key={shelf.id} shelf={shelf} />)}
          </CardList>
        )}
      </div>

      <CreateShelfDialog open={creating} onClose={() => setCreating(false)} />
    </div>
  );
}

function ShelfRow({ shelf }: { shelf: ShelfInfo }) {
  const navigate = useNavigate();
  const share = usedShare(shelf.usedBytes, shelf.quotaBytes);

  return (
    <ListButton icon={iconMap.shelf} onClick={() => navigate(shelfRoute(shelf.name))}>
      <div className="flex-1 min-w-0 space-y-1 text-left">
        <div className="text-body-lg font-medium text-text-1-light dark:text-text-1-dark truncate">{shelf.name}</div>
        <div className="text-body-md text-text-3-light dark:text-text-3-dark">{describeUsage(shelf)}</div>
        {share !== null && <ProgressBar value={share} variant={share >= 0.9 ? 'danger' : 'brand'} label={`${shelf.name} usage`} />}
      </div>
    </ListButton>
  );
}

function CreateShelfDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const createShelf = useCreateShelf();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [quota, setQuota] = useState('');
  const { error, setError, submit } = useSubmit('Could not create the shelf');

  function close(): void {
    setName('');
    setQuota('');
    setError(undefined);
    onClose();
  }

  function create(): Promise<void> {
    return submit(() => createShelf.mutateAsync({ name: name.trim(), quotaBytes: parseQuotaMegabytes(quota) }), (created) => {
      toast.success(`Created ${created.name}`);
      close();
      navigate(shelfRoute(created.name));
    });
  }

  return (
    <Dialog open={open} title="New shelf" onClose={close} onSubmit={() => void create()} submitLabel="Create" submitting={createShelf.isPending} error={error}>
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="receipts" autoComplete="off" required />
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Lowercase letters, digits, '-', '_' and '.'; it appears in links.</p>
      <Input label="Quota in MiB" value={quota} onChange={(event) => setQuota(event.target.value)} placeholder="No limit" inputMode="decimal" autoComplete="off" />
    </Dialog>
  );
}
