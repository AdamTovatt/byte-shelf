import { useState } from 'react';
import { Button, Icon, ListButton, Spinner, iconMap } from '../ui';
import { FormDialog } from './FormDialog';
import { useListing, useMoveEntries } from '../hooks/useShelfQueries';
import { useSubmit } from '../hooks/useSubmit';
import { parentPath, pathSegments, ROOT_PATH } from '../lib/shelfPaths';
import { nameOrCount, type Entry } from '../services/shelfService';

interface MoveDialogProps {
  shelf: string;
  /** What to move: one row's entry, or a selection's, all from the folder shown. */
  entries: Entry[];
  onClose: () => void;
}

/**
 * Moves files and folders to another folder on the same shelf, keeping their names: the reader walks
 * the shelf's folders and picks one. A folder cannot go into itself, so a moving folder and what is
 * under it are left out. They move one at a time; a refusal stops there and names what it stopped
 * at, and what moved before it stays moved.
 */
export function MoveDialog({ shelf, entries, onClose }: MoveDialogProps) {
  const moveEntries = useMoveEntries(shelf);
  const from = parentPath(entries[0].value.path);
  const [folder, setFolder] = useState(from);
  const { data: listing, isLoading } = useListing(shelf, folder);
  const { error, submitting, submit } = useSubmit('Could not move it');

  const isMovingOrInside = (path: string) =>
    entries.some((entry) => entry.kind === 'directory' && (path === entry.value.path || path.startsWith(`${entry.value.path}/`)));
  const folders = (listing?.directories ?? []).map((listed) => listed.directory).filter((directory) => !isMovingOrInside(directory.path));
  const alreadyThere = entries.every((entry) => parentPath(entry.value.path) === folder);
  const title = `Move ${nameOrCount(entries)}`;

  return (
    <FormDialog
      open
      title={title}
      onClose={onClose}
      onSubmit={() => void submit(() => moveEntries(entries, folder), onClose)}
      submitDisabled={alreadyThere}
      submitLabel="Move here"
      submitting={submitting}
      error={error}
    >
      {/* Not the library's Breadcrumbs: those are links to routes, and this trail picks a folder in
          the dialog's own state without leaving the page. */}
      <nav aria-label="Folder to move to" className="flex flex-wrap items-center gap-1 text-body-md">
        <Button type="button" variant="text" onClick={() => setFolder(ROOT_PATH)} className={folder === ROOT_PATH ? 'font-medium text-fg' : 'text-muted'}>
          {shelf}
        </Button>
        {pathSegments(folder).map((segment) => (
          <span key={segment.path} className="flex items-center gap-1">
            <span aria-hidden="true" className="text-neutral-8">/</span>
            <Button type="button" variant="text" onClick={() => setFolder(segment.path)} className={segment.path === folder ? 'font-medium text-fg' : 'text-muted'}>
              {segment.name}
            </Button>
          </span>
        ))}
      </nav>

      <div className="max-h-64 overflow-y-auto rounded-lg border border-border">
        {isLoading && <div className="p-3"><Spinner size="sm" /></div>}
        {listing && folders.length === 0 && <p className="p-3 text-body-md text-muted">No folders in here.</p>}
        {folders.map((directory) => (
          <ListButton
            key={directory.id}
            icon={iconMap.folder}
            onClick={() => setFolder(directory.path)}
            className="gap-3 px-3 py-2 text-body-md text-fg hover:bg-row-hover"
            actions={<Icon icon={iconMap.chevronRight} size={14} className="pointer-events-none shrink-0 text-neutral-9" />}
          >
            <span className="block truncate">{directory.name}</span>
          </ListButton>
        ))}
      </div>

      <p className="text-body-sm text-muted">
        {alreadyThere ? `${entries.length === 1 ? 'It is' : 'They are'} in this folder now: pick another.` : 'Nothing already there is replaced.'}
      </p>
    </FormDialog>
  );
}
