import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, Card, EmptyState, Icon, Input, PageLayout, ProgressBar, Spinner, iconMap } from '../ui';
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { QuotaField } from '../components/QuotaField';
import { useCreateShelf, useShelves } from '../hooks/useShelfQueries';
import { useIsAdmin } from '../hooks/useUserQueries';
import { describeUsage, parseQuota, usedShare, type QuotaUnit } from '../lib/format';
import { shelfRoute } from '../lib/shelfPaths';
import { useSubmit } from '../hooks/useSubmit';
import type { ShelfInfo } from '../services/shelfService';

/** The shelves the user reaches, with their usage; admins create new ones here. */
export function ShelvesPage() {
  const { data: shelves, isLoading, error } = useShelves();
  const isAdmin = useIsAdmin();
  const [creating, setCreating] = useState(false);

  const newShelf = isAdmin && (
    <Button variant="main" icon={<Icon icon={iconMap.add} size={16} />} onClick={() => setCreating(true)}>
      New shelf
    </Button>
  );

  return (
    <PageLayout className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-heading-md text-fg">Shelves</h1>
        {newShelf}
      </div>

      {isLoading && <Spinner />}
      <LoadError error={error} failure="Could not load the shelves" />
      {shelves && shelves.length === 0 && (
        <EmptyState
          message={isAdmin ? 'No shelves yet. A shelf holds files for one purpose, with its own folders, quota and access.' : 'You have not been given access to any shelf yet.'}
        />
      )}
      {shelves && shelves.length > 0 && (
        <div className="grid gap-3 sm:grid-cols-2">
          {shelves.map((shelf) => <ShelfCard key={shelf.id} shelf={shelf} />)}
        </div>
      )}

      <CreateShelfDialog open={creating} onClose={() => setCreating(false)} />
    </PageLayout>
  );
}

function ShelfCard({ shelf }: { shelf: ShelfInfo }) {
  const navigate = useNavigate();
  const share = usedShare(shelf.usedBytes, shelf.quotaBytes);

  return (
    <Card
      variant="interactive"
      padding="none"
      role="link"
      aria-label={shelf.name}
      onClick={() => navigate(shelfRoute(shelf.name))}
      className="p-4"
    >
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-surface text-muted">
          <Icon icon={iconMap.shelf} size={18} />
        </span>
        <div className="min-w-0 flex-1 space-y-1">
          <div className="truncate text-body-lg font-medium text-fg">{shelf.name}</div>
          <div className="text-body-md text-muted">{describeUsage(shelf)}</div>
        </div>
      </div>
      {share !== null && (
        <ProgressBar
          size="sm"
          progress={share * 100}
          tone={share >= 0.9 ? 'danger' : 'brand'}
          label={`${shelf.name} usage`}
          className="mt-4"
        />
      )}
    </Card>
  );
}

function CreateShelfDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const createShelf = useCreateShelf();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [quota, setQuota] = useState('');
  const [unit, setUnit] = useState<QuotaUnit>('GiB');
  const { error, setError, submit } = useSubmit('Could not create the shelf');

  function close(): void {
    setName('');
    setQuota('');
    setError(undefined);
    onClose();
  }

  function create(): Promise<void> {
    return submit(() => createShelf.mutateAsync({ name: name.trim(), quotaBytes: parseQuota(quota, unit) }), (created) => {
      toast.success(`Created ${created.name}`);
      close();
      navigate(shelfRoute(created.name));
    });
  }

  return (
    <FormDialog open={open} title="New shelf" onClose={close} onSubmit={() => void create()} submitLabel="Create" submitting={createShelf.isPending} error={error}>
      <div className="space-y-1.5">
        <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="receipts" autoComplete="off" required />
        <p className="text-body-sm text-muted">Lowercase letters, digits, '-', '_' and '.'; it appears in links.</p>
      </div>
      <QuotaField amount={quota} unit={unit} onAmountChange={setQuota} onUnitChange={setUnit} />
    </FormDialog>
  );
}
