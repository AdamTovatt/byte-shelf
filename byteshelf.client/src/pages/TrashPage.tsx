import { useState } from 'react';
import { useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, CardList, CardListItem, Dialog, Spinner } from '../components/ui';
import { Icon } from '../components/ui/Icon';
import { ShelfHeader } from '../components/ShelfHeader';
import { useEmptyTrash, useRestoreDeletion, useTrash } from '../hooks/useShelfQueries';
import { iconMap } from '../lib/iconMap';
import { formatBytes, formatTime } from '../lib/format';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import type { TrashEntry } from '../services/shelfService';

/** What was deleted from a shelf and can still be restored, newest first. */
export function TrashPage() {
  const shelf = useParams().shelf!;
  const { data: trash, isLoading } = useTrash(shelf);
  const restore = useRestoreDeletion(shelf);
  const [emptying, setEmptying] = useState(false);

  function onRestore(entry: TrashEntry): Promise<void> {
    return runOrToast(async () => {
      await restore.mutateAsync(entry.deletionId);
      toast.success(`Restored ${entry.path}`);
    }, 'Could not restore it');
  }

  return (
    <div className="p-6 md:p-8">
      <div className="max-w-4xl mx-auto space-y-6">
        <ShelfHeader shelf={shelf} view="trash" />

        {isLoading && <Spinner />}
        {trash && trash.length === 0 && <p className="text-body-lg text-text-3-light dark:text-text-3-dark">The trash is empty.</p>}
        {trash && trash.length > 0 && (
          <>
            <div className="flex justify-end">
              <Button variant="outlineDanger" onClick={() => setEmptying(true)}>Empty trash</Button>
            </div>
            <CardList>
              {trash.map((entry) => (
                <CardListItem key={entry.deletionId}>
                  <div className="flex items-center gap-3">
                    <Icon icon={entry.kind === 'directory' ? iconMap.folder : iconMap.file} size={18} className="shrink-0 text-text-3-light dark:text-text-3-dark" />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-body-lg text-text-1-light dark:text-text-1-dark">{entry.path}</div>
                      <div className="text-body-sm text-text-3-light dark:text-text-3-dark">
                        {describeDeletion(entry)}
                      </div>
                    </div>
                    <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.restore} size={16} />} onClick={() => void onRestore(entry)}>
                      Restore
                    </Button>
                  </div>
                </CardListItem>
              ))}
            </CardList>
          </>
        )}
      </div>

      {emptying && <EmptyTrashDialog shelf={shelf} onClose={() => setEmptying(false)} />}
    </div>
  );
}

function describeDeletion(entry: TrashEntry): string {
  const contents = entry.kind === 'directory' ? `${entry.fileCount} ${entry.fileCount === 1 ? 'file' : 'files'}, ` : '';
  return `${contents}${formatBytes(entry.sizeBytes)} · deleted ${formatTime(entry.deletedAt)} · purged ${formatTime(entry.purgesAt)}`;
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
    <Dialog open title="Empty the trash?" onClose={onClose} onSubmit={() => void empty()} submitLabel="Empty trash" submitVariant="danger" submitting={emptyTrash.isPending} error={error}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">Everything in this shelf's trash is deleted for good. It cannot be restored afterwards.</p>
    </Dialog>
  );
}
