import { useState } from 'react';
import { useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, EmptyState, Icon, PageLayout, Spinner, Tooltip, getFileIcon, iconMap } from '../ui';
import { CardList } from '../components/CardList';
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { NameLine } from '../components/MiddleTruncatedName';
import { ShelfHeader } from '../components/ShelfHeader';
import { fileOpening } from '../components/fileOpening';
import { useEncryption } from '../hooks/useEncryption';
import { useEmptyTrash, useRestoreDeletion, useTrash } from '../hooks/useShelfQueries';
import { deletedContent } from '../services/contentService';
import { formatBytes, formatDate } from '../lib/format';
import { nameOf } from '../lib/shelfPaths';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { fetchDeletion, type TrashEntry } from '../services/shelfService';

/**
 * What was deleted from a shelf and can still be restored, newest first. A deleted file can be saved
 * without restoring it, here as on a replica, where a file purged on the primary may still be.
 */
export function TrashPage() {
  const shelf = useParams().shelf!;
  const { data: trash, isLoading, error } = useTrash(shelf);
  const restore = useRestoreDeletion(shelf);
  const encryption = useEncryption(shelf);
  const [emptying, setEmptying] = useState(false);

  function onRestore(entry: TrashEntry): Promise<void> {
    return runOrToast(async () => {
      await restore.mutateAsync(entry.deletionId);
      toast.success(<NameLine before="Restored" name={entry.path} />);
    }, 'Could not restore it');
  }

  /** Saves the file a file deletion took, as the files page saves a live one. */
  function onDownload(entry: TrashEntry): Promise<void> {
    return runOrToast(async () => {
      const deletion = await fetchDeletion(shelf, entry.deletionId);
      const file = deletion.tree.files.find((taken) => taken.id === entry.entryId);
      if (!file) throw new Error(`${entry.path} is no longer in the trash.`);
      await fileOpening(deletedContent(shelf, deletion), encryption.contents).save([file]);
    }, 'Could not save the file');
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
          <CardList className="overflow-hidden">
            {trash.map((entry) => (
              <div key={entry.deletionId} className="flex items-center gap-3 px-4 py-3">
                <Icon icon={entry.kind === 'directory' ? iconMap.folder : getFileIcon(nameOf(entry.path))} size={18} className="shrink-0 text-muted" />
                <div className="min-w-0 flex-1">
                  <div className="truncate text-body-md text-fg">{entry.path}</div>
                  <div className="flex items-center gap-1 text-body-sm text-muted">
                    <span>{describeDeletion(entry)}</span>
                    {entry.kind === 'directory' && <FolderDownloadMark />}
                  </div>
                </div>
                {/* Stacked on a phone, so the path and its details keep the row's width. */}
                <div className="flex shrink-0 flex-col items-stretch gap-2 sm:flex-row">
                  {entry.kind === 'file' && (
                    <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.download} size={14} />} onClick={() => void onDownload(entry)} aria-label={`Download ${entry.path}`}>
                      Download
                    </Button>
                  )}
                  <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.restore} size={14} />} onClick={() => void onRestore(entry)} aria-label={`Restore ${entry.path}`}>
                    Restore
                  </Button>
                </div>
              </div>
            ))}
          </CardList>
        </>
      )}

      {emptying && <EmptyTrashDialog shelf={shelf} onClose={() => setEmptying(false)} />}
      {encryption.dialog}
    </PageLayout>
  );
}

const FOLDER_DOWNLOAD = 'Download here saves one file. To save a deleted folder whole, use the command line or an agent.';

/** The ⓘ on a deleted folder's row, saying where it is downloaded from: the page saves files only, as the files page does. */
function FolderDownloadMark() {
  return (
    <Tooltip content={FOLDER_DOWNLOAD} position="top" maxWidth={280} as="span">
      <span role="img" tabIndex={0} aria-label={FOLDER_DOWNLOAD} className="flex items-center p-1.5 text-muted">
        <Icon icon={iconMap.info} size={14} />
      </span>
    </Tooltip>
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
