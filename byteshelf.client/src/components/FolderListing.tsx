import { useEffect, useMemo, useRef, useState, type DragEvent, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
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
import { FileDetailsDialog } from './FileDetailsDialog';
import { FolderIcon } from './FolderIcon';
import { FormDialog } from './FormDialog';
import { LoadError } from './LoadError';
import { PagedResults } from './PagedResults';
import { CardList } from './CardList';
import { MoveDialog } from './MoveDialog';
import { ShelfHeader } from './ShelfHeader';
import { PREFETCHED_SUBFOLDERS, useCreateDirectory, useDeleteEntry, useDuplicateSearch, useFileSearch, useListing, useMoveEntry, usePrefetchListing, useRestoreDeletion, useSetDescription } from '../hooks/useShelfQueries';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { useCreateDownloadLink } from '../hooks/useLinkQueries';
import { useSelection, type SelectionClickModifiers } from '../hooks/useSelection';
import { useFoundFiles } from '../hooks/useFoundFiles';
import { useFilePicker } from '../hooks/useFilePicker';
import { MobileTopBarActions, MobileTopBarOverlay } from './layout/MobileTopBar';
import { encryptionActions } from '../hooks/useEncryption';
import type { WayToViewer } from '../hooks/useFolderView';
import { usePluginHost } from '../hooks/usePluginHost';
import type { ShelfWork } from '../hooks/useShelfWork';
import { FileActionItems } from './FileActionItems';
import { openFile, opensInTab, saveFiles } from './fileOpening';
import { FilePreview } from './preview/FilePreview';
import { useShelfPreview } from '../hooks/useShelfPreview';
import { isTypingTarget } from '../lib/keys';
import { openable } from '../services/fileReading';
import { showActionToast } from './actionToast';
import { NameAndReason, NameLine } from './MiddleTruncatedName';
import { reasonOf, runOrToastNamed, toastNamedFailure } from './nameToasts';
import { DndContext, DragOverlay, pointerWithin } from '@dnd-kit/core';
import { DraggableEntryRow, DroppableCrumb, EntryDragPreview } from './EntryDragAndDrop';
import { useEntryDragAndDrop } from '../hooks/useEntryDragAndDrop';
import { formatBytes, formatTime } from '../lib/format';
import type { FolderType } from '../lib/plugins/folderType';
import { folderTypeOf } from '../lib/plugins/folderTypeLookup';
import { folderListingRoute, joinPath, parentPath, relativeTo, shelfRoute } from '../lib/shelfPaths';
import { copyWhenReady } from '../lib/clipboard';
import { runOrToast } from '../lib/utils';
import { useIsOverlayOpen } from '../ui/hooks/useOverlay';
import { useSubmit } from '../hooks/useSubmit';
import { directoryEntry, entrySizeBytes, nameOrCount, type DuplicateGroup, type DuplicateSearchResults, type Entry, type FileSearchResults, type ShelfFile } from '../services/shelfService';
import { SearchOptions } from './SearchOptions';
import { NO_SEARCH_FILTERS, isSearch, searchCriteria } from '../lib/searchFilters';

interface FolderListingProps {
  shelf: string;
  path: string;
  work: ShelfWork;
  /** The way to the viewer this is shown instead of; null for none. */
  toViewer: WayToViewer | null;
}

// The listing's toasts and dialogs stay here rather than in modules of their own: each is small and serves
// it alone, so apart they would only be moved, not made simpler or shared.
/** What is in a directory, uploading into it, and renaming, moving and deleting what is there. */
export function FolderListing({ shelf, path, work: { passwords, encryption, reading, uploads, fileActions }, toViewer }: FolderListingProps) {
  const navigate = useNavigate();
  const { plugins } = usePluginHost();
  const { data: listing, isLoading, error } = useListing(shelf, path);
  const openableHere = (file: ShelfFile) => openable({ file, reading });
  const overlayOpen = useIsOverlayOpen();
  const deleteEntry = useDeleteEntry(shelf);
  const restore = useRestoreDeletion(shelf);
  const createDownloadLink = useCreateDownloadLink(shelf);
  const [creatingDirectory, setCreatingDirectory] = useState(false);
  const [renaming, setRenaming] = useState<Entry | null>(null);
  const [moving, setMoving] = useState<Entry[] | null>(null);
  const [describing, setDescribing] = useState<ShelfFile | null>(null);
  const [detailing, setDetailing] = useState<ShelfFile | null>(null);
  const [dragging, setDragging] = useState(false);
  // Kept with the folder it was typed in, so opening another starts with no search and no filter.
  const [typed, setTyped] = useState({ path, text: '', filters: NO_SEARCH_FILTERS });
  const typedHere = typed.path === path;
  const searchText = typedHere ? typed.text : '';
  const filters = typedHere ? typed.filters : NO_SEARCH_FILTERS;
  const searching = isSearch(searchText, filters);
  const settledText = useDebouncedValue(searchText, SEARCH_DELAY_MS);
  const criteria = searchCriteria(settledText, filters);
  const found = useFileSearch(shelf, path, criteria, isSearch(settledText, filters) && !filters.duplicates);
  const duplicates = useDuplicateSearch(shelf, path, criteria, filters.duplicates);

  /** Uploads into this folder; in an encrypted one, encrypted with its password, asked for if this tab has not unlocked it. */
  async function upload(files: File[]): Promise<void> {
    const folder = listing?.encryptedBy;
    if (!folder) return uploads.upload(files, path);
    await runOrToast(async () => {
      const password = await passwords.folderPassword(folder);
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

  /** Opens a row: a folder by going into it, a file in the preview when a viewer shows it or one still loading may, else in a new tab. */
  function open(entry: Entry): void {
    if (entry.kind === 'directory') navigate(shelfRoute(shelf, entry.value.path));
    else preview.open(entry.value);
  }

  /**
   * Copies a link that lets anyone open the file without signing in, for the server's default of a
   * day, until it is revoked in the shelf's settings (Links). Where it cannot be copied at once (plain
   * HTTP), the toast holds it in a field to copy from.
   */
  async function copyLink(file: ShelfFile): Promise<void> {
    const link = createDownloadLink.mutateAsync(file.id).then((created) => ({ ...created, url: new URL(created.url, window.location.origin).href }));
    const copied = copyWhenReady(link.then((created) => created.url));

    await runOrToast(async () => {
      const { url, expiresAt } = await link;
      const until = <span>Anyone with it can open it until {formatTime(expiresAt)}, unless it is revoked in the shelf's settings, under Links.</span>;
      if (await copied) {
        toast.success(<NameAndReason before="Copied a link to" name={file.name} after="." reason={until} />);
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
        if (removing.length === 1) toast.error(getErrorMessage(caught, 'Could not delete it'));
        else toastNamedFailure('Could not delete', entry.value.name, reasonOf(caught));
      }
    }
    if (removed.length === 0) return;

    showActionToast({
      message: <NameLine before="Moved" name={nameOrCount(removed.map(({ entry }) => entry))} after=" to the trash" />,
      action: 'Undo',
      icon: <Icon icon={iconMap.undo} size={14} />,
      onAction: () => {
        for (const { entry, deletionId } of removed) {
          void runOrToastNamed(() => restore.mutateAsync(deletionId), 'Could not restore', entry.value.name);
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
  // other as the pointer or focus reaches it (onPrefetch below). Only as it opens: the listing is
  // refetched after every change on the shelf, and fetching them all again each time would send up
  // to PREFETCHED_SUBFOLDERS more requests for each change.
  const prefetchListing = usePrefetchListing(shelf);
  const prefetchedFor = useRef<string | null>(null);
  useEffect(() => {
    if (!listing || prefetchedFor.current === path) return;
    prefetchedFor.current = path;
    listing.directories.slice(0, PREFETCHED_SUBFOLDERS).forEach((listed) => prefetchListing(listed.directory.path));
  }, [path, listing, prefetchListing]);

  // Only the folder's own list selects; search results open on a click as before. A search hides
  // the list, so it lists nothing to select while it shows, and the selection is dropped as it starts.
  const entryIds = useMemo(() => (searching ? [] : entries.map((entry) => entry.value.id)), [entries, searching]);
  const selection = useSelection(entryIds, path);
  const selected = entries.filter((entry) => selection.isSelected(entry.value.id));
  const selectedFiles = selected.flatMap((entry) => (entry.kind === 'file' ? [entry.value] : []));
  const drag = useEntryDragAndDrop({ shelf, entries, selected });

  // The preview goes through the files shown: the folder's own, or the search's results so far.
  const foundFiles = useFoundFiles(found, filesOfResults);
  const duplicateFiles = useFoundFiles(duplicates, filesOfGroups);
  const shownFiles = !searching ? { files: listing?.files ?? [] } : filters.duplicates ? duplicateFiles : foundFiles;
  const preview = useShelfPreview(shelf, shownFiles, reading);

  // ⌘A or Ctrl+A selects every row, unless a field has the keys; Escape clears the selection. A
  // dialog keeps both for itself.
  const { active: selecting, clear: clearSelection, selectAll } = selection;
  const canSelectAll = entryIds.length > 0;
  useEffect(() => {
    if (overlayOpen) return;
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
  }, [overlayOpen, selecting, clearSelection, selectAll, canSelectAll]);

  function row(entry: Entry, options: { label?: string; selectable?: boolean } = {}) {
    // A link would hand on only encrypted bytes, so an encrypted file has none. Only a listing's rows
    // know whether they are under an encrypted folder.
    const encrypted = entry.value.encrypted;
    const type = entry.kind === 'directory' ? folderTypeOf(plugins, entry.value.name)?.type ?? null : null;
    const encrypting = encryptionActions(entry, !options.selectable ? 'unknown' : listing?.encryptedBy ? 'encrypted' : 'plain');
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
        opensIn={entry.kind === 'file' && preview.showsInPreview(entry.value) ? 'preview' : 'tab'}
        type={type}
        onOpen={() => open(entry)}
        onShowContents={type ? () => navigate(folderListingRoute(shelf, entry.value.path)) : undefined}
        onOpenInTab={entry.kind === 'file' && opensInTab(entry.value) ? () => void openFile(openableHere(entry.value)) : undefined}
        onPrefetch={entry.kind === 'directory' ? () => prefetchListing(entry.value.path) : undefined}
        onDownload={entry.kind === 'file' ? () => void saveFiles([openableHere(entry.value)]) : undefined}
        onDescribe={entry.kind === 'file' ? () => setDescribing(entry.value) : undefined}
        onShowDetails={entry.kind === 'file' ? () => setDetailing(entry.value) : undefined}
        onCopyLink={entry.kind === 'file' && !encrypted ? () => void copyLink(entry.value) : undefined}
        onEncrypt={encrypting.encrypt ? () => void encryption.encrypt(entry) : undefined}
        onRemoveEncryption={encrypting.removeEncryption ? () => void encryption.removeEncryption(entry) : undefined}
        pluginItems={entry.kind === 'file' && <FileActionItems actions={fileActions} target={openableHere(entry.value)} />}
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

  /** A file a search found, named by its path from this folder. */
  function foundRow(file: ShelfFile) {
    return row({ kind: 'file', value: file }, { label: relativeTo(path, file.path) });
  }

  /** Clears the text; the filters stay on until they are turned off where they were turned on. */
  function clearSearch(): void {
    setTyped({ path, text: '', filters });
  }

  /**
   * The search field, in its row from md up or in the phone's top bar; both show the one search.
   * Escape clears its text, and the options at its end hold the filters.
   */
  function searchField() {
    return (
      <Input
        variant="quiet"
        value={searchText}
        onChange={(event) => setTyped({ path, text: event.target.value, filters })}
        onKeyDown={(event) => event.key === 'Escape' && clearSearch()}
        placeholder="Search"
        aria-label="Search files in this folder and below"
        icon={<Icon icon={iconMap.search} size={16} />}
        endElement={(
          <div className="flex items-center">
            {searchText && <IconButton variant="ghost" size="sm" icon={iconMap.close} aria-label="Clear the search" onClick={clearSearch} />}
            <SearchOptions filters={filters} onChange={(changed) => setTyped({ path, text: searchText, filters: changed })} />
          </div>
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
    onDownload: selectedFiles.length === selected.length ? () => void saveFiles(selectedFiles.map(openableHere)) : undefined,
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
          menuItem={toViewer?.by === 'choice' ? { label: `Show as ${toViewer.type.label}`, icon: (size) => <FolderIcon type={toViewer.type} size={size} />, onChoose: toViewer.show } : undefined}
          wrapFolderCrumb={(folderPath, crumb) => <DroppableCrumb folderPath={folderPath} disabled={drag.dragged === null || folderPath === path}>{crumb}</DroppableCrumb>}
        />

        {/* From md up, search and Add have a row of their own, which the selection bar covers rather
            than replaces, so the list below never moves when a row is picked. On a phone they sit in
            the top bar instead, and the selection's actions cover that bar. */}
        <div className="relative hidden md:block">
          <div className={cn('flex items-center gap-2', selecting && 'invisible')}>
            <div className="min-w-0 flex-1">{searchField()}</div>
            {toViewer?.by === 'password' && <Button variant="secondary" icon={<Icon icon={iconMap.unlock} size={16} />} onClick={toViewer.unlock}>Unlock</Button>}
            <Dropdown align="right" trigger={<Button variant="main" icon={<Icon icon={iconMap.add} size={16} />}>Add</Button>}>
              {addItems}
            </Dropdown>
          </div>
          {selecting && <SelectionBar placement="row" {...selectionActions} />}
        </div>
        <MobileTopBarActions>
          <div className="min-w-0 flex-1">{searchField()}</div>
          {toViewer?.by === 'password' && <IconButton variant="secondary" icon={iconMap.unlock} aria-label="Unlock" onClick={toViewer.unlock} />}
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
          filters.duplicates
            ? (
              <PagedResults
                query={duplicates}
                itemsOf={(page) => page.groups}
                failure="Could not look for duplicates"
                empty={noDuplicatesFound(searchText.trim(), filters.undescribed)}
                className="space-y-4"
              >
                {(groups) => groups.map((group) => <DuplicateGroupList key={group.sha256} group={group} row={foundRow} />)}
              </PagedResults>
            )
            : (
              <PagedResults
                query={found}
                itemsOf={(page) => page.files}
                failure="Could not search"
                empty={noFileFound(searchText.trim(), filters.undescribed)}
                className="space-y-2"
              >
                {(files) => <CardList className="overflow-hidden">{files.map(foundRow)}</CardList>}
              </PagedResults>
            )
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
      {detailing && <FileDetailsDialog shelf={shelf} file={detailing} onClose={() => setDetailing(null)} />}
      {preview.isOpen && <FilePreview {...preview.shown} detailsOn={shelf} actions={fileActions} />}
    </div>
  );
}

/** The files on a page of a search. */
function filesOfResults(page: FileSearchResults): ShelfFile[] {
  return page.files;
}

/** The files on a page of a search for duplicates, a group after another. */
function filesOfGroups(page: DuplicateSearchResults): ShelfFile[] {
  return page.groups.flatMap((group) => group.files);
}

/** How long typing must pause before a search is sent. */
const SEARCH_DELAY_MS = 250;

/** What the search's text is matched against: a file without a description has only the rest. */
function matchedFields(undescribed: boolean): string {
  return undescribed ? 'name, title or camera' : 'name, description, title or camera';
}

/** What an empty search says it looked for. */
function noFileFound(text: string, undescribed: boolean): string {
  if (!text) return 'Every file here and below has a description.';
  const without = undescribed ? ' without a description' : '';
  return `No file here or below${without} has “${text}” in its ${matchedFields(undescribed)}.`;
}

/** What an empty search for duplicates says it looked among. */
function noDuplicatesFound(text: string, undescribed: boolean): string {
  const without = undescribed ? ' without a description' : '';
  if (!text) return `No two files here or below${without} have the same contents.`;
  return `No files here or below${without} with “${text}” in their ${matchedFields(undescribed)} have the same contents.`;
}

/** One group of files with the same contents: how many copies and bytes, then a row per file. */
function DuplicateGroupList({ group, row }: { group: DuplicateGroup; row: (file: ShelfFile) => ReactNode }) {
  return (
    <section className="space-y-1.5">
      <h2 className="text-body-sm text-muted">
        {group.files.length} copies of {formatBytes(group.sizeBytes)}, {formatBytes(group.repeatedBytes)} repeated
      </h2>
      <CardList className="overflow-hidden">
        {group.files.map(row)}
      </CardList>
    </section>
  );
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
 * What can be done to the selected rows, over the toolbar while any are selected. On a narrow screen
 * the buttons keep only their icons, their names read out but not shown, so all of them fit on a
 * phone.
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
  /** Where a file opens: over the page, or in a tab of its own. */
  opensIn: 'preview' | 'tab';
  /** For a folder: its type, whose viewer opens it; null for a plain one. */
  type: FolderType | null;
  onOpen: () => void;
  /** For a folder of a type: shows its listing. */
  onShowContents?: () => void;
  /** For a file a tab shows. */
  onOpenInTab?: () => void;
  /** For a folder: fetch what it holds ahead, as the pointer or focus reaches the row. */
  onPrefetch?: () => void;
  onDownload?: () => void;
  onDescribe?: () => void;
  /** For a file: its record and what its contents say. */
  onShowDetails?: () => void;
  onCopyLink?: () => void;
  onEncrypt?: () => void;
  onRemoveEncryption?: () => void;
  /** For a file: what plugins add to its menu. */
  pluginItems?: ReactNode;
  onRename: () => void;
  onMove: () => void;
  onDelete: () => void;
}

function EntryRow({ entry, label, selection, opensIn, type, onOpen, onShowContents, onOpenInTab, onPrefetch, onDownload, onDescribe, onShowDetails, onCopyLink, onEncrypt, onRemoveEncryption, pluginItems, onRename, onMove, onDelete }: EntryRowProps) {
  const isFile = entry.kind === 'file';
  const name = entry.value.name;
  const selected = selection?.selected ?? false;
  const openName = isFile ? `Open ${name}${opensIn === 'tab' ? ' in a new tab' : ''}` : `Open the folder ${name}`;

  // A selected row takes a translucent wash of the selection blue behind its usual text, so the text
  // keeps its colours, and `data-active` lets its list hide the lines beside it.
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
        title={selection ? `${openName}: double-click or press Enter` : isFile && opensIn === 'tab' ? 'Opens in a new tab' : undefined}
        aria-label={selection ? label ?? name : openName}
        aria-pressed={selection ? selected : undefined}
        className="flex min-w-0 flex-1 select-none items-center gap-3 rounded-lg px-2 py-2 text-left"
      >
        {isFile ? <Icon icon={getFileIcon(name)} size={18} className="shrink-0 text-muted" /> : <FolderIcon type={type} size={18} className="shrink-0 text-muted" />}
        <span className="min-w-0 truncate text-body-md text-fg">{label ?? name}</span>
        {entry.value.encrypted && <Icon icon={iconMap.lock} size={14} className="shrink-0 text-muted" aria-label="Encrypted" />}
        {isFile && (
          <span className="ml-auto hidden shrink-0 pl-2 text-body-sm text-muted sm:inline">
            {formatBytes(entry.value.sizeBytes)} · {formatTime(entry.value.updatedAt)}
          </span>
        )}
      </button>
      <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${name}`} />}>
        {onShowContents && <DropdownItem icon={<Icon icon={iconMap.folder} size={16} />} onClick={onShowContents}>Show contents</DropdownItem>}
        {onOpenInTab && <DropdownItem icon={<Icon icon={iconMap.openInTab} size={16} />} onClick={onOpenInTab}>Open in new tab</DropdownItem>}
        {onDownload && <DropdownItem icon={<Icon icon={iconMap.download} size={16} />} onClick={onDownload}>Download</DropdownItem>}
        {onCopyLink && <DropdownItem icon={<Icon icon={iconMap.copy} size={16} />} onClick={onCopyLink}>Copy link</DropdownItem>}
        {onShowDetails && <DropdownItem icon={<Icon icon={iconMap.info} size={16} />} onClick={onShowDetails}>Details</DropdownItem>}
        {onDescribe && <DropdownItem icon={<Icon icon={iconMap.edit} size={16} />} onClick={onDescribe}>Description</DropdownItem>}
        {onEncrypt && <DropdownItem icon={<Icon icon={iconMap.lock} size={16} />} onClick={onEncrypt}>Encrypt</DropdownItem>}
        {onRemoveEncryption && <DropdownItem icon={<Icon icon={iconMap.unlock} size={16} />} onClick={onRemoveEncryption}>Remove encryption</DropdownItem>}
        {pluginItems}
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
