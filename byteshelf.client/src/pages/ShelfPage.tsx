import { useEffect, useMemo, useState, type DragEvent, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import {
  Button,
  CopyField,
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
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { CardList } from '../components/CardList';
import { MoveDialog } from '../components/MoveDialog';
import { ShelfHeader } from '../components/ShelfHeader';
import { PREFETCHED_SUBFOLDERS, useCreateDirectory, useDeleteEntry, useFileSearch, useListing, useMoveEntry, usePrefetchListing, useRestoreDeletion, useSetDescription } from '../hooks/useShelfQueries';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useSelection, type SelectionClickModifiers } from '../hooks/useSelection';
import { useFilePicker } from '../hooks/useFilePicker';
import { MobileTopBarActions, MobileTopBarOverlay } from '../components/layout/MobileTopBar';
import { useUploads } from '../hooks/useUploads';
import { encryptionActions, useEncryption } from '../hooks/useEncryption';
import { saveBlob, saveFrom, showBlob } from '../lib/localBlobs';
import { openTabWithoutPasswords } from '../lib/encryption/passwords';
import { showActionToast } from '../components/actionToast';
import { NameLine } from '../components/MiddleTruncatedName';
import { DndContext, DragOverlay, pointerWithin } from '@dnd-kit/core';
import { DraggableEntryRow, DroppableCrumb, EntryDragPreview } from '../components/EntryDragAndDrop';
import { useEntryDragAndDrop } from '../hooks/useEntryDragAndDrop';
import { formatBytes, formatTime } from '../lib/format';
import { joinPath, parentPath, pathFromRoute, relativeTo, shelfRoute } from '../lib/shelfPaths';
import { copyWhenReady } from '../lib/clipboard';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { createDownloadLink, directoryEntry, entrySizeBytes, type Entry, type FileSearchResults, type ShelfFile } from '../services/shelfService';

// Its toasts and dialogs stay here with the page rather than in their own modules: splitting them out
// would only reshape working code (final review, item 51).
/** One directory on a shelf: what is in it, uploading into it, and renaming, moving and deleting what is there. */
export function ShelfPage() {
  const params = useParams();
  const shelf = params.shelf!;
  const path = pathFromRoute(params['*']);
  const navigate = useNavigate();
  const { data: listing, isLoading, error } = useListing(shelf, path);
  const uploads = useUploads(shelf);
  const encryption = useEncryption(shelf);
  const deleteEntry = useDeleteEntry(shelf);
  const restore = useRestoreDeletion(shelf);
  const [creatingDirectory, setCreatingDirectory] = useState(false);
  const [renaming, setRenaming] = useState<Entry | null>(null);
  const [moving, setMoving] = useState<Entry[] | null>(null);
  const [describing, setDescribing] = useState<ShelfFile | null>(null);
  const [dragging, setDragging] = useState(false);
  // Kept with the shelf and folder it was typed in, so opening another starts with no search: the
  // route, and so this component, stays the same from one shelf to the next.
  const [typed, setTyped] = useState({ shelf, path, text: '' });
  const searchText = typed.shelf === shelf && typed.path === path ? typed.text : '';
  const searching = searchText.trim() !== '';
  const found = useFileSearch(shelf, path, useDebouncedValue(searchText, SEARCH_DELAY_MS));

  /** Uploads into this folder; in an encrypted one, encrypted with its password, asked for if this tab has not unlocked it. */
  async function upload(files: File[]): Promise<void> {
    const folder = listing?.encryptedBy;
    if (!folder) return uploads.upload(files, path);
    await runOrToast(async () => {
      const password = await encryption.folderPassword(folder);
      if (password !== null) uploads.upload(files, path, password);
    }, 'Could not upload');
  }

  const picker = useFilePicker((files) => void upload(files));

  function onDrop(event: DragEvent<HTMLDivElement>): void {
    event.preventDefault();
    setDragging(false);
    const files = Array.from(event.dataTransfer.files);
    if (files.length > 0) void upload(files);
  }

  /**
   * Opens a file in a new tab, through a link that works only for the few minutes a browser needs to
   * start: a plain link cannot carry the session's token. The tab is opened before the request, while
   * the click still counts as the user's, or the browser would block it as a popup. A blocked tab
   * saves the file instead.
   */
  async function openInTab(file: ShelfFile): Promise<void> {
    if (file.encrypted) return openDecrypted(file);
    const tab = openTabWithoutPasswords();
    if (!tab) return save([file]);
    await runOrToast(async () => {
      try {
        tab.location.href = (await createDownloadLink(shelf, file.id, OPEN_LINK_MINUTES)).url;
      } catch (caught) {
        tab.close();
        throw caught;
      }
    }, 'Could not open the file');
  }

  /**
   * Saves files through the same short links. Each is a download rather than a navigation, so one
   * starting does not cancel the one before it; a browser may ask once before saving several.
   */
  async function save(files: ShelfFile[]): Promise<void> {
    await runOrToast(async () => {
      for (const file of files) {
        if (file.encrypted) {
          const plain = await encryption.contents(file);
          if (plain) saveBlob(plain, file.name);
          continue;
        }
        saveFrom(`${(await createDownloadLink(shelf, file.id, OPEN_LINK_MINUTES)).url}?download=true`, '');
      }
    }, files.length === 1 ? 'Could not save the file' : 'Could not save the files');
  }

  /**
   * Opens an encrypted file from its contents decrypted here, never through a link. Asking for the
   * password can take long enough for the browser to block the tab; a toast then opens it from a
   * fresh click.
   */
  async function openDecrypted(file: ShelfFile): Promise<void> {
    await runOrToast(async () => {
      const plain = await encryption.contents(file);
      if (!plain || showBlob(plain, file.name)) return;
      showActionToast({ message: <NameLine name={file.name} after=" is unlocked" />, action: 'Open', onAction: () => showBlob(plain, file.name), duration: 15000 });
    }, 'Could not open the file');
  }

  /** Opens a row: a folder by going into it, a file in a new tab. */
  function open(entry: Entry): void {
    if (entry.kind === 'directory') navigate(shelfRoute(shelf, entry.value.path));
    else void openInTab(entry.value);
  }

  /**
   * Copies a link that lets anyone open the file without signing in, for the server's default of a
   * day. Where it cannot be copied at once (plain HTTP), the toast holds it in a field to copy from.
   */
  async function copyLink(file: ShelfFile): Promise<void> {
    const link = createDownloadLink(shelf, file.id).then((created) => ({ ...created, url: new URL(created.url, window.location.origin).href }));
    const copied = copyWhenReady(link.then((created) => created.url));

    await runOrToast(async () => {
      const { url, expiresAt } = await link;
      const until = <span>Anyone with it can open it until {formatTime(expiresAt)}.</span>;
      if (await copied) {
        toast.success(
          <span className="flex min-w-0 flex-col">
            <NameLine before="Copied a link to" name={file.name} after="." />
            {until}
          </span>,
        );
      } else {
        toast((t) => (
          <span className="flex min-w-0 flex-col gap-2">
            <span>{until}</span>
            <CopyField value={url} copyButtonAriaLabel="Copy the link" />
            <Button size="sm" variant="secondary" className="self-start" onClick={() => toast.dismiss(t.id)}>Dismiss</Button>
          </span>
        ), { duration: Infinity });
      }
    }, 'Could not make a link');
  }

  /**
   * Moves entries to the trash at once; the toast's Undo restores them. Emptying the trash is what
   * cannot be undone. Each is its own deletion, so one refused leaves the others in the trash, and
   * Undo restores those.
   */
  async function remove(removing: Entry[]): Promise<void> {
    const removed: { entry: Entry; deletionId: string }[] = [];
    for (const entry of removing) {
      try {
        removed.push({ entry, deletionId: (await deleteEntry.mutateAsync({ kind: entry.kind, id: entry.value.id })).deletionId });
      } catch (caught) {
        toast.error(removing.length === 1
          ? getErrorMessage(caught, 'Could not delete it')
          : (
            <span className="flex min-w-0 flex-col">
              <NameLine before="Could not delete" name={entry.value.name} after=":" />
              <span>{getErrorMessage(caught, 'it was refused')}</span>
            </span>
          ));
      }
    }
    if (removed.length === 0) return;

    showActionToast({
      message: <NameLine before="Moved" name={removed.length === 1 ? removed[0].entry.value.name : `${removed.length} items`} after=" to the trash" />,
      action: 'Undo',
      icon: <Icon icon={iconMap.undo} size={14} />,
      onAction: () => {
        for (const { entry, deletionId } of removed) {
          void runOrToast(() => restore.mutateAsync(deletionId), `Could not restore ${entry.value.name}`);
        }
      },
      duration: 8000,
    });
  }

  const entries: Entry[] = useMemo(() => (listing
    ? [
        ...listing.directories.map(directoryEntry),
        ...listing.files.map((value): Entry => ({ kind: 'file', value })),
      ]
    : []), [listing]);

  // Opening a subfolder should be instant: fetch the first ones ahead as this folder opens, and any
  // other as the pointer or focus reaches it (onPrefetch below).
  const prefetchListing = usePrefetchListing(shelf);
  useEffect(() => {
    listing?.directories.slice(0, PREFETCHED_SUBFOLDERS).forEach((listed) => prefetchListing(listed.directory.path));
  }, [listing, prefetchListing]);

  // Only the folder's own list selects; search results open on a click as before. A search hides
  // the list, so it lists nothing to select while it shows.
  const entryIds = useMemo(() => (searching ? [] : entries.map((entry) => entry.value.id)), [entries, searching]);
  const selection = useSelection(entryIds, `${shelf}/${path}`);
  const selected = entries.filter((entry) => selection.isSelected(entry.value.id));
  const selectedFiles = selected.flatMap((entry) => (entry.kind === 'file' ? [entry.value] : []));
  const drag = useEntryDragAndDrop({ shelf, entries, selected });
  const dialogOpen = creatingDirectory || renaming !== null || moving !== null || describing !== null;

  // ⌘A or Ctrl+A selects every row, unless a field has the keys; Escape clears the selection. A
  // dialog keeps both for itself.
  const { active: selecting, clear: clearSelection, selectAll } = selection;
  const canSelectAll = entryIds.length > 0;
  useEffect(() => {
    if (dialogOpen) return;
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && selecting) {
        clearSelection();
      } else if (event.key.toLowerCase() === 'a' && (event.metaKey || event.ctrlKey) && !event.altKey && canSelectAll && !isTypingTarget(event.target)) {
        event.preventDefault();
        selectAll();
      }
    }
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [dialogOpen, selecting, clearSelection, selectAll, canSelectAll]);

  function row(entry: Entry, options: { label?: string; selectable?: boolean } = {}) {
    // A link would hand on only encrypted bytes, so an encrypted file has none. Only a listing's rows
    // know whether they are under an encrypted folder.
    const encrypted = entry.value.encrypted;
    const actions = encryptionActions(entry, !options.selectable ? 'unknown' : listing?.encryptedBy ? 'encrypted' : 'plain');
    const shown = (
      <EntryRow
        key={entry.value.id}
        entry={entry}
        label={options.label}
        selection={options.selectable ? {
          selected: selection.isSelected(entry.value.id),
          onSelectClick: (modifiers) => selection.handleSelectClick(entry.value.id, modifiers),
          onToggle: () => selection.toggle(entry.value.id),
        } : undefined}
        onOpen={() => open(entry)}
        onPrefetch={entry.kind === 'directory' ? () => prefetchListing(entry.value.path) : undefined}
        onDownload={entry.kind === 'file' ? () => void save([entry.value]) : undefined}
        onDescribe={entry.kind === 'file' ? () => setDescribing(entry.value) : undefined}
        onCopyLink={entry.kind === 'file' && !encrypted ? () => void copyLink(entry.value) : undefined}
        onEncrypt={actions.encrypt ? () => void encryption.encrypt(entry) : undefined}
        onRemoveEncryption={actions.removeEncryption ? () => void encryption.removeEncryption(entry) : undefined}
        onRename={() => setRenaming(entry)}
        onMove={() => setMoving([entry])}
        onDelete={() => void remove([entry])}
      />
    );
    // Only the folder's own rows drag, as only they select; search results stay where they are.
    return options.selectable
      ? <DraggableEntryRow key={entry.value.id} entry={entry} droppable={drag.takesDrop(entry)}>{shown}</DraggableEntryRow>
      : shown;
  }

  function clearSearch(): void {
    setTyped({ shelf, path, text: '' });
  }

  /** The search field, in its row from md up or in the phone's top bar; both show the one search. Escape clears it. */
  function searchField() {
    return (
      <Input
        variant="quiet"
        value={searchText}
        onChange={(event) => setTyped({ shelf, path, text: event.target.value })}
        onKeyDown={(event) => event.key === 'Escape' && clearSearch()}
        placeholder="Search"
        aria-label="Search files in this folder and below"
        icon={<Icon icon={iconMap.search} size={16} />}
        endElement={searchText && (
          <IconButton variant="ghost" size="sm" icon={iconMap.close} aria-label="Clear the search" onClick={clearSearch} />
        )}
        autoComplete="off"
      />
    );
  }

  const addItems = (
    <>
      <DropdownItem icon={<Icon icon={iconMap.upload} size={16} />} onClick={picker.open}>Upload file</DropdownItem>
      <DropdownItem icon={<Icon icon={iconMap.folder} size={16} />} onClick={() => setCreatingDirectory(true)}>New folder</DropdownItem>
    </>
  );

  const selectionActions = {
    count: selection.count,
    sizeBytes: selected.reduce((total, entry) => total + entrySizeBytes(entry), 0),
    onDownload: selectedFiles.length === selected.length ? () => void save(selectedFiles) : undefined,
    onMove: () => setMoving(selected),
    onDelete: () => void remove(selected),
    onClear: clearSelection,
  };

  return (
    <div
      className="min-h-full"
      onDragOver={(event) => {
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={(event) => event.currentTarget === event.target && setDragging(false)}
      onDrop={onDrop}
      // A click in empty space clears the selection, as Escape does and as in Finder. Menus and
      // dialogs are portals: React hands their clicks up to here, but they are not inside this page.
      onClick={(event) => {
        const target = event.target as Element;
        if (selecting && event.currentTarget.contains(target) && !target.closest('[data-selectable-row], [role="toolbar"]')) clearSelection();
      }}
    >
      <PageLayout className="space-y-6">
        <DndContext sensors={drag.sensors} accessibility={{ announcements: drag.announcements }} collisionDetection={pointerWithin} onDragStart={drag.onDragStart} onDragEnd={drag.onDragEnd} onDragCancel={drag.onDragCancel}>
        <ShelfHeader
          shelf={shelf}
          view="files"
          path={path}
          wrapFolderCrumb={(folderPath, crumb) => <DroppableCrumb folderPath={folderPath} disabled={drag.dragged === null || folderPath === path}>{crumb}</DroppableCrumb>}
        />

        {/* From md up, search and Add have a row of their own, which the selection bar covers rather
            than replaces, so the list below never moves when a row is picked. On a phone they sit in
            the top bar instead, and the selection's actions cover that bar. */}
        <div className="relative hidden md:block">
          <div className={cn('flex items-center gap-2', selecting && 'invisible')}>
            <div className="min-w-0 flex-1">{searchField()}</div>
            <Dropdown align="right" trigger={<Button variant="main" icon={<Icon icon={iconMap.add} size={16} />}>Add</Button>}>
              {addItems}
            </Dropdown>
          </div>
          {selecting && <SelectionBar placement="row" {...selectionActions} />}
        </div>
        <MobileTopBarActions>
          <div className="min-w-0 flex-1">{searchField()}</div>
          <Dropdown align="right" trigger={<IconButton variant="main" icon={iconMap.add} aria-label="Add" />}>
            {addItems}
          </Dropdown>
        </MobileTopBarActions>
        {selecting && (
          <MobileTopBarOverlay>
            <SelectionBar placement="topBar" {...selectionActions} />
          </MobileTopBarOverlay>
        )}
        {picker.input}

        {searching ? (
          <SearchResults results={found.data} isLoading={found.isLoading} error={found.error} text={searchText.trim()} row={(file) => row({ kind: 'file', value: file }, { label: relativeTo(path, file.path) })} />
        ) : (
          <>
            {isLoading && <Spinner />}
            <LoadError error={error} failure="Could not list this directory" />
            {listing && entries.length === 0 && (
              <EmptyState
                message={dragging ? 'Drop to upload into this folder.' : 'Nothing here yet. Upload files, or drop them anywhere on the page.'}
                className={cn('rounded-xl border-2 border-dashed', dragging ? 'border-brand-border' : 'border-transparent')}
              />
            )}
            {entries.length > 0 && (
              <CardList hideDividersBesideLitRows className={cn('overflow-hidden', dragging && 'ring-2 ring-brand-border')}>
                {entries.map((entry) => row(entry, { selectable: true }))}
              </CardList>
            )}
          </>
        )}
        <DragOverlay dropAnimation={null}>{drag.dragged && <EntryDragPreview entries={drag.dragged} />}</DragOverlay>
        </DndContext>
      </PageLayout>

      <NewDirectoryDialog shelf={shelf} parent={path} open={creatingDirectory} onClose={() => setCreatingDirectory(false)} />
      {renaming && <RenameDialog shelf={shelf} entry={renaming} onClose={() => setRenaming(null)} />}
      {moving && <MoveDialog shelf={shelf} entries={moving} onClose={() => setMoving(null)} />}
      {describing && <DescriptionDialog shelf={shelf} file={describing} onClose={() => setDescribing(null)} />}
      {encryption.dialog}
    </div>
  );
}

/** How long typing must pause before a search is sent. */
const SEARCH_DELAY_MS = 250;

/** How long a link to open or save a file works: enough for a browser to start, or a viewer to fetch ranges. */
const OPEN_LINK_MINUTES = 5;

interface SearchResultsProps {
  results: FileSearchResults | undefined;
  isLoading: boolean;
  error: Error | null;
  text: string;
  row: (file: ShelfFile) => ReactNode;
}

function SearchResults({ results, isLoading, error, text, row }: SearchResultsProps) {
  if (error) return <LoadError error={error} failure="Could not search" />;
  if (isLoading || !results) return <Spinner />;
  if (results.files.length === 0) return <EmptyState message={`No file here or below has “${text}” in its name or description.`} />;

  return (
    <div className="space-y-2">
      <CardList className="overflow-hidden">
        {results.files.map(row)}
      </CardList>
      {results.nextCursor && <p className="text-body-sm text-muted">Showing the first {results.files.length} matches. Type more to narrow them.</p>}
    </div>
  );
}

/** Whether a key pressed here belongs to a field, as ⌘A selecting its text does. */
function isTypingTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
}

interface SelectionBarProps {
  /** Over the search row from md up, or over the phone's top bar. */
  placement: 'row' | 'topBar';
  count: number;
  /** The size of every selected file and of every file under a selected folder. */
  sizeBytes: number;
  /** Saves the selection; absent when it holds a folder, which cannot be saved as one file. */
  onDownload?: () => void;
  onMove: () => void;
  onDelete: () => void;
  onClear: () => void;
}

/**
 * What can be done to the selected rows, over the toolbar while any are selected. As ordo's
 * FileSelectionToolbar. On a narrow screen the buttons keep only their icons, their names read out
 * but not shown, so all of them fit on a phone.
 */
function SelectionBar({ placement, count, sizeBytes, onDownload, onMove, onDelete, onClear }: SelectionBarProps) {
  const label = (text: string) => <span className="sr-only sm:not-sr-only">{text}</span>;

  return (
    <div
      role="toolbar"
      aria-label="Selected items"
      className={cn('absolute inset-0 flex items-center gap-2', placement === 'row' ? 'rounded-lg bg-surface px-3' : 'bg-panel px-4')}
    >
      <span className="whitespace-nowrap text-body-sm text-fg">{count} selected ({formatBytes(sizeBytes)})</span>
      <div className="flex-1" />
      {onDownload && (
        <Button variant="secondary" size="sm" icon={<Icon icon={iconMap.download} size={16} />} onClick={onDownload}>{label('Download')}</Button>
      )}
      <Button variant="secondary" size="sm" icon={<Icon icon={iconMap.move} size={16} />} onClick={onMove}>{label('Move')}</Button>
      <Button variant="outlineDanger" size="sm" icon={<Icon icon={iconMap.delete} size={16} />} onClick={onDelete}>{label('Delete')}</Button>
      <IconButton variant="ghost" size="sm" icon={iconMap.close} aria-label="Clear the selection" onClick={onClear} />
    </div>
  );
}

/** A row's part in a selection: a click selects, a double-click or Enter opens. */
interface RowSelection {
  selected: boolean;
  onSelectClick: (modifiers: SelectionClickModifiers) => void;
  onToggle: () => void;
}

interface EntryRowProps {
  entry: Entry;
  /** What the row is called: the name, unless a search shows the path from the folder. */
  label?: string;
  /** Given, the row selects on a click and opens on a double-click; otherwise a click opens it. */
  selection?: RowSelection;
  onOpen: () => void;
  /** For a folder: fetch what it holds ahead, as the pointer or focus reaches the row. */
  onPrefetch?: () => void;
  onDownload?: () => void;
  onDescribe?: () => void;
  onCopyLink?: () => void;
  onEncrypt?: () => void;
  onRemoveEncryption?: () => void;
  onRename: () => void;
  onMove: () => void;
  onDelete: () => void;
}

function EntryRow({ entry, label, selection, onOpen, onPrefetch, onDownload, onDescribe, onCopyLink, onEncrypt, onRemoveEncryption, onRename, onMove, onDelete }: EntryRowProps) {
  const isFile = entry.kind === 'file';
  const name = entry.value.name;
  const selected = selection?.selected ?? false;
  const openName = isFile ? `Open ${name} in a new tab` : `Open the folder ${name}`;

  // As ordo's file rows: a selected row takes a translucent wash of the selection blue behind its
  // usual text, and `data-active` lets its list hide the lines beside it.
  return (
    <div
      data-selectable-row={selection ? true : undefined}
      data-active={selected || undefined}
      className={cn('flex items-center gap-2 px-2 py-1 transition-colors', selected ? 'bg-selected/15 hover:bg-selected/20' : 'hover:bg-row-hover')}
    >
      <button
        type="button"
        onClick={selection ? (event) => selection.onSelectClick({ shiftKey: event.shiftKey, metaKey: event.metaKey, ctrlKey: event.ctrlKey }) : onOpen}
        onDoubleClick={selection ? onOpen : undefined}
        onPointerEnter={onPrefetch}
        onFocus={onPrefetch}
        // Enter opens, as a double-click does, and Space toggles the row; both stop the button's own
        // click, which would select the row alone.
        onKeyDown={selection ? (event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            onOpen();
          } else if (event.key === ' ') {
            event.preventDefault();
            selection.onToggle();
          }
        } : undefined}
        title={selection ? `${openName}: double-click or press Enter` : isFile ? 'Opens in a new tab' : undefined}
        aria-label={selection ? label ?? name : openName}
        aria-pressed={selection ? selected : undefined}
        className="flex min-w-0 flex-1 select-none items-center gap-3 rounded-lg px-2 py-2 text-left"
      >
        <Icon icon={isFile ? getFileIcon(name) : iconMap.folder} size={18} className="shrink-0 text-muted" />
        <span className="min-w-0 truncate text-body-md text-fg">{label ?? name}</span>
        {entry.value.encrypted && <Icon icon={iconMap.lock} size={14} className="shrink-0 text-muted" aria-label="Encrypted" />}
        {isFile && (
          <span className="ml-auto hidden shrink-0 pl-2 text-body-sm text-muted sm:inline">
            {formatBytes(entry.value.sizeBytes)} · {formatTime(entry.value.updatedAt)}
          </span>
        )}
      </button>
      <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${name}`} />}>
        {onDownload && <DropdownItem icon={<Icon icon={iconMap.download} size={16} />} onClick={onDownload}>Download</DropdownItem>}
        {onCopyLink && <DropdownItem icon={<Icon icon={iconMap.copy} size={16} />} onClick={onCopyLink}>Copy link</DropdownItem>}
        {onDescribe && <DropdownItem icon={<Icon icon={iconMap.edit} size={16} />} onClick={onDescribe}>Description</DropdownItem>}
        {onEncrypt && <DropdownItem icon={<Icon icon={iconMap.lock} size={16} />} onClick={onEncrypt}>Encrypt</DropdownItem>}
        {onRemoveEncryption && <DropdownItem icon={<Icon icon={iconMap.unlock} size={16} />} onClick={onRemoveEncryption}>Remove encryption</DropdownItem>}
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
