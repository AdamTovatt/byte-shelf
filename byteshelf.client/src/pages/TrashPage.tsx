import { useState } from 'react';
import { useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, Card, EmptyState, Icon, PageLayout, Spinner, getFileIcon, iconMap } from '../ui';
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { ShelfHeader } from '../components/ShelfHeader';
import { useEmptyTrash, useRestoreDeletion, useTrash } from '../hooks/useShelfQueries';
import { formatBytes, formatDate } from '../lib/format';
import { nameOf } from '../lib/shelfPaths';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import type { TrashEntry } from '../services/shelfService';

/** What was deleted from a shelf and can still be restored, newest first. */
export function TrashPage() {
  const shelf = useParams().shelf!;
  const { data: trash, isLoading, error } = useTrash(shelf);
  const restore = useRestoreDeletion(shelf);
  const [emptying, setEmptying] = useState(false);

  function onRestore(entry: TrashEntry): Promise<void> {
    return runOrToast(async () => {
      await restore.mutateAsync(entry.deletionId);
      toast.success(`Restored ${entry.path}`);
    }, 'Could not restore it');
  }

  return (
    <PageLayout className="space-y-6">
      <ShelfHeader shelf={shelf} view="trash" />

      {isLoading && <Spinner />}
      <LoadError error={error} failure="Could not load the trash" />
      {trash && trash.length === 0 && (
        <EmptyState message="The trash is empty. Deleted files and folders wait here until they are deleted for good, and can be restored until then." />
      )}
      {trash && trash.length > 0 && (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-body-md text-muted">Each item is deleted for good on the date shown, and can be restored until then.</p>
            <Button variant="outlineDanger" onClick={() => setEmptying(true)}>Empty trash</Button>
          </div>
          <Card variant="elevated" padding="none" className="divide-y divide-border overflow-hidden">
            {trash.map((entry) => (
              <div key={entry.deletionId} className="flex items-center gap-3 px-4 py-3">
                <Icon icon={entry.kind === 'directory' ? iconMap.folder : getFileIcon(nameOf(entry.path))} size={18} className="shrink-0 text-muted" />
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-md text-fg">{entry.path}</div>
                  <div className="text-body-sm text-muted">{describeDeletion(entry)}</div>
                </div>
                <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.restore} size={14} />} onClick={() => void onRestore(entry)} aria-label={`Restore ${entry.path}`}>
                  Restore
                </Button>
              </div>
            ))}
          </Card>
        </>
      )}

      {emptying && <EmptyTrashDialog shelf={shelf} onClose={() => setEmptying(false)} />}
    </PageLayout>
  );
}

function describeDeletion(entry: TrashEntry): string {
  const contents = entry.kind === 'directory' ? `${entry.fileCount} ${entry.fileCount === 1 ? 'file' : 'files'}, ` : '';
  return `${contents}${formatBytes(entry.sizeBytes)} · deleted ${formatDate(entry.deletedAt)} · deleted for good on ${formatDate(entry.purgesAt)}`;
}

function EmptyTrashDialog({ shelf, onClose }: { shelf: string; onClose: () => void }) {
  const emptyTrash = useEmptyTrash(shelf);
  const { error, submit } = useSubmit('Could not empty the trash');

  function empty(): Promise<void> {
    return submit(() => emptyTrash.mutateAsync(), () => {
      toast.success('Emptied the trash');
      onClose();
    });
  }

  return (
    <FormDialog open title="Empty the trash?" onClose={onClose} onSubmit={() => void empty()} submitLabel="Empty trash" submitVariant="danger" submitting={emptyTrash.isPending} error={error}>
      <p className="text-body-md text-fg">Everything in this shelf's trash is deleted for good. It cannot be restored afterwards.</p>
    </FormDialog>
  );
}
