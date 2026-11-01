import { useState, type DragEvent, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import {
  Button,
  Card,
  Dropdown,
  DropdownItem,
  EmptyState,
  Icon,
  IconButton,
  Input,
  PageLayout,
  Spinner,
  Textarea,
  cn,
  getErrorMessage,
  getFileIcon,
  iconMap,
} from '../ui';
import { FileButton } from '../components/FileButton';
import { FormDialog } from '../components/FormDialog';
import { MoveDialog } from '../components/MoveDialog';
import { ShelfHeader } from '../components/ShelfHeader';
import { useCreateDirectory, useDeleteEntry, useFileSearch, useListing, useMoveEntry, useRestoreDeletion, useSetDescription } from '../hooks/useShelfQueries';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useUploads } from '../hooks/useUploads';
import { formatBytes, formatTime } from '../lib/format';
import { joinPath, parentPath, pathFromRoute, relativeTo, shelfRoute } from '../lib/shelfPaths';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { createDownloadLink, type FileSearchResults, type ShelfDirectory, type ShelfFile } from '../services/shelfService';

export type Entry = { kind: 'directory'; value: ShelfDirectory } | { kind: 'file'; value: ShelfFile };

/** One directory on a shelf: what is in it, uploading into it, and renaming, moving and deleting what is there. */
export function ShelfPage() {
  const params = useParams();
  const shelf = params.shelf!;
  const path = pathFromRoute(params['*']);
  const navigate = useNavigate();
  const { data: listing, isLoading, error } = useListing(shelf, path);
  const uploads = useUploads(shelf);
  const deleteEntry = useDeleteEntry(shelf);
  const restore = useRestoreDeletion(shelf);
  const [creatingDirectory, setCreatingDirectory] = useState(false);
  const [renaming, setRenaming] = useState<Entry | null>(null);
  const [moving, setMoving] = useState<Entry | null>(null);
  const [describing, setDescribing] = useState<ShelfFile | null>(null);
  const [dragging, setDragging] = useState(false);
  // Kept with the shelf and folder it was typed in, so opening another starts with no search: the
  // route, and so this component, stays the same from one shelf to the next.
  const [typed, setTyped] = useState({ shelf, path, text: '' });
  const searchText = typed.shelf === shelf && typed.path === path ? typed.text : '';
  const searching = searchText.trim() !== '';
  const found = useFileSearch(shelf, path, useDebouncedValue(searchText, SEARCH_DELAY_MS));

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
      } catch (caught) {
        tab?.close();
        throw caught;
      }
    }, 'Could not open the file');
  }

  /** Moves an entry to the trash at once; the toast's Undo restores it. Emptying the trash is what cannot be undone. */
  async function remove(entry: Entry): Promise<void> {
    let deletionId: string;
    try {
      deletionId = (await deleteEntry.mutateAsync({ kind: entry.kind, id: entry.value.id })).deletionId;
    } catch (caught) {
      toast.error(getErrorMessage(caught, 'Could not delete it'));
      return;
    }

    toast((t) => (
      <span className="flex items-center gap-3">
        <span>Moved {entry.value.name} to the trash</span>
        <Button
          size="sm"
          variant="secondary"
          icon={<Icon icon={iconMap.undo} size={14} />}
          onClick={() => {
            toast.dismiss(t.id);
            void runOrToast(() => restore.mutateAsync(deletionId), `Could not restore ${entry.value.name}`);
          }}
        >
          Undo
        </Button>
      </span>
    ), { duration: 8000 });
  }

  const entries: Entry[] = listing
    ? [
        ...listing.directories.map((value): Entry => ({ kind: 'directory', value })),
        ...listing.files.map((value): Entry => ({ kind: 'file', value })),
      ]
    : [];

  function row(entry: Entry, label?: string) {
    return (
      <EntryRow
        key={entry.value.id}
        entry={entry}
        label={label}
        onOpen={() => (entry.kind === 'directory' ? navigate(shelfRoute(shelf, entry.value.path)) : void open(entry.value, false))}
        onDownload={entry.kind === 'file' ? () => void open(entry.value, true) : undefined}
        onDescribe={entry.kind === 'file' ? () => setDescribing(entry.value) : undefined}
        onRename={() => setRenaming(entry)}
        onMove={() => setMoving(entry)}
        onDelete={() => void remove(entry)}
      />
    );
  }

  return (
    <div
      className="min-h-full"
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(event) => event.currentTarget === event.target && setDragging(false)}
      onDrop={onDrop}
    >
      <PageLayout className="space-y-6">
        <ShelfHeader shelf={shelf} view="files" path={path} />

        <div className="flex flex-wrap items-center gap-2">
          <FileButton variant="main" icon={<Icon icon={iconMap.upload} size={16} />} onFiles={(files) => uploads.upload(files, path)} aria-label="Upload files">
            Upload
          </FileButton>
          <Button variant="secondary" icon={<Icon icon={iconMap.folder} size={16} />} onClick={() => setCreatingDirectory(true)} aria-label="New folder">
            New folder
          </Button>
          <div className="min-w-48 flex-1">
            <Input
              variant="quiet"
              value={searchText}
              onChange={(event) => setTyped({ shelf, path, text: event.target.value })}
              onKeyDown={(event) => event.key === 'Escape' && setTyped({ shelf, path, text: '' })}
              placeholder="Search names and descriptions"
              aria-label="Search files in this folder and below"
              icon={<Icon icon={iconMap.search} size={16} />}
              endElement={searchText && (
                <IconButton variant="ghost" size="sm" icon={iconMap.close} aria-label="Clear the search" onClick={() => setTyped({ shelf, path, text: '' })} />
              )}
              autoComplete="off"
            />
          </div>
        </div>

        {searching ? (
          <SearchResults results={found.data} isLoading={found.isLoading} error={found.error} text={searchText.trim()} row={(file) => row({ kind: 'file', value: file }, relativeTo(path, file.path))} />
        ) : (
          <>
            {isLoading && <Spinner />}
            {error && <p className="text-body-md text-danger-11">{getErrorMessage(error, 'Could not list this directory')}</p>}
            {listing && entries.length === 0 && (
              <EmptyState
                message={dragging ? 'Drop to upload into this folder.' : 'Nothing here yet. Upload files, or drop them anywhere on the page.'}
                className={cn('rounded-xl border-2 border-dashed', dragging ? 'border-brand-border' : 'border-transparent')}
              />
            )}
            {entries.length > 0 && (
              <Card variant="elevated" padding="none" className={cn('divide-y divide-border overflow-hidden', dragging && 'ring-2 ring-brand-border')}>
                {entries.map((entry) => row(entry))}
              </Card>
            )}
          </>
        )}
      </PageLayout>

      <NewDirectoryDialog shelf={shelf} parent={path} open={creatingDirectory} onClose={() => setCreatingDirectory(false)} />
      {renaming && <RenameDialog shelf={shelf} entry={renaming} onClose={() => setRenaming(null)} />}
      {moving && <MoveDialog shelf={shelf} entry={moving} onClose={() => setMoving(null)} />}
      {describing && <DescriptionDialog shelf={shelf} file={describing} onClose={() => setDescribing(null)} />}
    </div>
  );
}

/** How long typing must pause before a search is sent. */
const SEARCH_DELAY_MS = 250;

interface SearchResultsProps {
  results: FileSearchResults | undefined;
  isLoading: boolean;
  error: Error | null;
  text: string;
  row: (file: ShelfFile) => ReactNode;
}

function SearchResults({ results, isLoading, error, text, row }: SearchResultsProps) {
  if (error) return <p className="text-body-md text-danger-11">{getErrorMessage(error, 'Could not search')}</p>;
  if (isLoading || !results) return <Spinner />;
  if (results.files.length === 0) return <EmptyState message={`No file here or below has “${text}” in its name or description.`} />;

  return (
    <div className="space-y-2">
      <Card variant="elevated" padding="none" className="divide-y divide-border overflow-hidden">
        {results.files.map(row)}
      </Card>
      {results.nextCursor && <p className="text-body-sm text-muted">Showing the first {results.files.length} matches. Type more to narrow them.</p>}
    </div>
  );
}

interface EntryRowProps {
  entry: Entry;
  /** What the row is called: the name, unless a search shows the path from the folder. */
  label?: string;
  onOpen: () => void;
  onDownload?: () => void;
  onDescribe?: () => void;
  onRename: () => void;
  onMove: () => void;
  onDelete: () => void;
}

function EntryRow({ entry, label, onOpen, onDownload, onDescribe, onRename, onMove, onDelete }: EntryRowProps) {
  const isFile = entry.kind === 'file';
  const name = entry.value.name;
  const description = isFile ? entry.value.description : null;

  return (
    <div className="group flex items-center gap-2 px-2 py-1 transition-colors hover:bg-row-hover">
      <button
        type="button"
        onClick={onOpen}
        title={isFile ? 'Opens in a new tab' : undefined}
        aria-label={isFile ? `Open ${name} in a new tab` : `Open the folder ${name}`}
        className="flex min-w-0 flex-1 items-center gap-3 rounded-lg px-2 py-2 text-left"
      >
        <Icon icon={isFile ? getFileIcon(name) : iconMap.folder} size={18} className="shrink-0 text-muted" />
        <span className="flex min-w-0 flex-col">
          <span className="truncate text-body-md text-fg">{label ?? name}</span>
          {description && <span className="truncate text-body-sm text-muted" title={description}>{description}</span>}
        </span>
        {isFile && (
          <Icon icon={iconMap.externalLink} size={14} className="shrink-0 text-neutral-9 opacity-60 transition-opacity group-hover:opacity-100" />
        )}
      </button>
      {isFile && (
        <span className="hidden shrink-0 text-body-sm text-muted sm:inline">
          {formatBytes(entry.value.sizeBytes)} · {formatTime(entry.value.updatedAt)}
        </span>
      )}
      <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${name}`} />}>
        {onDownload && <DropdownItem icon={<Icon icon={iconMap.download} size={16} />} onClick={onDownload}>Download</DropdownItem>}
        {onDescribe && <DropdownItem icon={<Icon icon={iconMap.edit} size={16} />} onClick={onDescribe}>Description</DropdownItem>}
        <DropdownItem icon={<Icon icon={iconMap.rename} size={16} />} onClick={onRename}>Rename</DropdownItem>
        <DropdownItem icon={<Icon icon={iconMap.move} size={16} />} onClick={onMove}>Move</DropdownItem>
        <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={onDelete}>Delete</DropdownItem>
      </Dropdown>
    </div>
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
    <FormDialog open={open} title="New folder" onClose={close} onSubmit={() => void submit(() => createDirectory.mutateAsync(joinPath(parent, name.trim())), close)} submitLabel="Create" submitting={createDirectory.isPending} error={error}>
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" required />
    </FormDialog>
  );
}

function DescriptionDialog({ shelf, file, onClose }: { shelf: string; file: ShelfFile; onClose: () => void }) {
  const setDescription = useSetDescription(shelf);
  const [text, setText] = useState(file.description ?? '');
  const { error, submit } = useSubmit('Could not save the description');

  return (
    <FormDialog
      open
      title={`Describe ${file.name}`}
      onClose={onClose}
      onSubmit={() => void submit(() => setDescription.mutateAsync({ id: file.id, description: text.trim() || null }), onClose)}
      submitLabel="Save"
      submitting={setDescription.isPending}
      error={error}
    >
      <Textarea
        label="Description"
        value={text}
        onChange={(event) => setText(event.target.value)}
        placeholder="What the file is. Search finds it by these words too."
        autoResize
      />
    </FormDialog>
  );
}

/** The part of a file name before its extension, which is what a rename usually changes. */
function stemLength(name: string, kind: Entry['kind']): number {
  const dot = name.lastIndexOf('.');
  return kind === 'file' && dot > 0 ? dot : name.length;
}

function RenameDialog({ shelf, entry, onClose }: { shelf: string; entry: Entry; onClose: () => void }) {
  const moveEntry = useMoveEntry(shelf);
  const [name, setName] = useState(entry.value.name);
  const { error, submit } = useSubmit('Could not rename it');

  function rename(): Promise<void> {
    const toPath = joinPath(parentPath(entry.value.path), name.trim());
    return submit(() => moveEntry.mutateAsync({ kind: entry.kind, id: entry.value.id, toPath }), onClose);
  }

  return (
    <FormDialog open title={`Rename ${entry.value.name}`} onClose={onClose} onSubmit={() => void rename()} submitLabel="Rename" submitting={moveEntry.isPending} error={error}>
      <Input
        label="Name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        onFocus={(event) => event.currentTarget.setSelectionRange(0, stemLength(event.currentTarget.value, entry.kind))}
        autoComplete="off"
        required
      />
    </FormDialog>
  );
}
