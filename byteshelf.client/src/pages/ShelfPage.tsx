import { useState, type DragEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, CardList, Dialog, Dropdown, DropdownItem, FileButton, IconButton, Input, ListRow, Spinner } from '../components/ui';
import { Icon } from '../components/ui/Icon';
import { ShelfHeader } from '../components/ShelfHeader';
import { UploadList } from '../components/UploadList';
import { useCreateDirectory, useDeleteEntry, useListing, useMoveEntry } from '../hooks/useShelfQueries';
import { useUploads } from '../hooks/useUploads';
import { iconMap } from '../lib/iconMap';
import { formatBytes, formatTime } from '../lib/format';
import { joinPath, pathFromRoute, shelfRoute } from '../lib/shelfPaths';
import { getErrorMessage, runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { createDownloadLink, type ShelfDirectory, type ShelfFile } from '../services/shelfService';

type Entry = { kind: 'directory'; value: ShelfDirectory } | { kind: 'file'; value: ShelfFile };

/** One directory on a shelf: what is in it, uploading into it, and moving and deleting what is there. */
export function ShelfPage() {
  const params = useParams();
  const shelf = params.shelf!;
  const path = pathFromRoute(params['*']);
  const navigate = useNavigate();
  const { data: listing, isLoading, error } = useListing(shelf, path);
  const uploads = useUploads(shelf);
  const [creatingDirectory, setCreatingDirectory] = useState(false);
  const [moving, setMoving] = useState<Entry | null>(null);
  const [deleting, setDeleting] = useState<Entry | null>(null);
  const [dragging, setDragging] = useState(false);

  function onDrop(event: DragEvent<HTMLDivElement>): void {
    event.preventDefault();
    setDragging(false);
    const files = Array.from(event.dataTransfer.files);
    if (files.length > 0) uploads.upload(files, path);
  }

  /**
   * Opens a file in a new tab or saves it, through a short-lived link: a plain link cannot carry the
   * session's token. The tab is opened before the request, while the click still counts as the
   * user's, or the browser would block it as a popup.
   */
  async function open(file: ShelfFile, download: boolean): Promise<void> {
    const tab = download ? null : window.open('', '_blank');
    await runOrToast(async () => {
      try {
        const link = await createDownloadLink(shelf, file.id);
        if (tab) {
          tab.location.href = link.url;
        } else {
          window.location.assign(`${link.url}?download=true`);
        }
      } catch (error) {
        tab?.close();
        throw error;
      }
    }, 'Could not open the file');
  }

  const entries: Entry[] = listing
    ? [
        ...listing.directories.map((value): Entry => ({ kind: 'directory', value })),
        ...listing.files.map((value): Entry => ({ kind: 'file', value })),
      ]
    : [];

  return (
    <div
      className="p-6 md:p-8 min-h-full"
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(event) => event.currentTarget === event.target && setDragging(false)}
      onDrop={onDrop}
    >
      <div className="max-w-4xl mx-auto space-y-6">
        <ShelfHeader shelf={shelf} view="files" path={path} />

        <div className="flex flex-wrap gap-2">
          <FileButton variant="primary" icon={<Icon icon={iconMap.upload} size={18} />} onFiles={(files) => uploads.upload(files, path)}>
            Upload
          </FileButton>
          <Button variant="secondary" icon={<Icon icon={iconMap.folder} size={18} />} onClick={() => setCreatingDirectory(true)}>
            New folder
          </Button>
        </div>

        <UploadList items={uploads.items} onReplace={uploads.replace} onDismiss={uploads.dismiss} onClearFinished={uploads.clearFinished} />

        {isLoading && <Spinner />}
        {error && <p className="text-body-md text-danger-light dark:text-danger-dark">{getErrorMessage(error, 'Could not list this directory')}</p>}
        {listing && entries.length === 0 && (
          <p className="text-body-lg text-text-3-light dark:text-text-3-dark">
            {dragging ? 'Drop the files to upload them here.' : 'Nothing here yet. Upload files, or drop them on the page.'}
          </p>
        )}
        {entries.length > 0 && (
          <CardList className={dragging ? 'ring-2 ring-brand-primary-light dark:ring-brand-primary-dark' : undefined}>
            {entries.map((entry) => (
              <EntryRow
                key={entry.value.id}
                entry={entry}
                onOpen={() => (entry.kind === 'directory' ? navigate(shelfRoute(shelf, entry.value.path)) : void open(entry.value, false))}
                onDownload={entry.kind === 'file' ? () => void open(entry.value, true) : undefined}
                onMove={() => setMoving(entry)}
                onDelete={() => setDeleting(entry)}
              />
            ))}
          </CardList>
        )}
      </div>

      <NewDirectoryDialog shelf={shelf} parent={path} open={creatingDirectory} onClose={() => setCreatingDirectory(false)} />
      {moving && <MoveDialog shelf={shelf} entry={moving} onClose={() => setMoving(null)} />}
      {deleting && <DeleteDialog shelf={shelf} entry={deleting} onClose={() => setDeleting(null)} />}
    </div>
  );
}

interface EntryRowProps {
  entry: Entry;
  onOpen: () => void;
  onDownload?: () => void;
  onMove: () => void;
  onDelete: () => void;
}

function EntryRow({ entry, onOpen, onDownload, onMove, onDelete }: EntryRowProps) {
  const details = entry.kind === 'file' ? `${formatBytes(entry.value.sizeBytes)} · ${formatTime(entry.value.updatedAt)}` : '';

  return (
    <ListRow
      icon={entry.kind === 'directory' ? iconMap.folder : iconMap.file}
      onClick={onOpen}
      actions={
        <>
          <span className="hidden sm:inline text-body-sm text-text-3-light dark:text-text-3-dark">{details}</span>
          <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${entry.value.name}`} />}>
            {onDownload && <DropdownItem icon={<Icon icon={iconMap.download} size={16} />} onClick={onDownload}>Download</DropdownItem>}
            <DropdownItem icon={<Icon icon={iconMap.edit} size={16} />} onClick={onMove}>Move or rename</DropdownItem>
            <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={onDelete}>Delete</DropdownItem>
          </Dropdown>
        </>
      }
    >
      <span className="block truncate text-body-lg text-text-1-light dark:text-text-1-dark">{entry.value.name}</span>
    </ListRow>
  );
}

function NewDirectoryDialog({ shelf, parent, open, onClose }: { shelf: string; parent: string; open: boolean; onClose: () => void }) {
  const createDirectory = useCreateDirectory(shelf);
  const [name, setName] = useState('');
  const { error, setError, submit } = useSubmit('Could not create the folder');

  function close(): void {
    setName('');
    setError(undefined);
    onClose();
  }

  return (
    <Dialog open={open} title="New folder" onClose={close} onSubmit={() => void submit(() => createDirectory.mutateAsync(joinPath(parent, name.trim())), close)} submitLabel="Create" submitting={createDirectory.isPending} error={error}>
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" required />
    </Dialog>
  );
}

function MoveDialog({ shelf, entry, onClose }: { shelf: string; entry: Entry; onClose: () => void }) {
  const moveEntry = useMoveEntry(shelf);
  const [toPath, setToPath] = useState(entry.value.path);
  const { error, submit } = useSubmit('Could not move it');

  function move(): Promise<void> {
    return submit(() => moveEntry.mutateAsync({ kind: entry.kind, id: entry.value.id, toPath: toPath.trim() }), onClose);
  }

  return (
    <Dialog open title={`Move or rename ${entry.value.name}`} onClose={onClose} onSubmit={() => void move()} submitLabel="Move" submitting={moveEntry.isPending} error={error}>
      <Input label="New path" value={toPath} onChange={(event) => setToPath(event.target.value)} autoComplete="off" required />
      <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Missing folders are created. Nothing already there is replaced.</p>
    </Dialog>
  );
}

function DeleteDialog({ shelf, entry, onClose }: { shelf: string; entry: Entry; onClose: () => void }) {
  const deleteEntry = useDeleteEntry(shelf);
  const { error, submit } = useSubmit('Could not delete it');

  function remove(): Promise<void> {
    return submit(() => deleteEntry.mutateAsync({ kind: entry.kind, id: entry.value.id }), () => {
      toast.success(`Moved ${entry.value.name} to the trash`);
      onClose();
    });
  }

  const what = entry.kind === 'directory' ? 'This folder and everything in it go' : 'It goes';
  return (
    <Dialog open title={`Delete ${entry.value.name}?`} onClose={onClose} onSubmit={() => void remove()} submitLabel="Delete" submitVariant="danger" submitting={deleteEntry.isPending} error={error}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">{what} to the trash, where it can be restored until it is purged.</p>
    </Dialog>
  );
}
