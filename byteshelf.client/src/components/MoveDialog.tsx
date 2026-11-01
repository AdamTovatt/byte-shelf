import { useState } from 'react';
import { Button, Icon, Spinner, iconMap } from '../ui';
import { FormDialog } from './FormDialog';
import { useListing, useMoveEntry } from '../hooks/useShelfQueries';
import { useSubmit } from '../hooks/useSubmit';
import { joinPath, parentPath, pathSegments, ROOT_PATH } from '../lib/shelfPaths';
import type { Entry } from '../services/shelfService';

interface MoveDialogProps {
  shelf: string;
  entry: Entry;
  onClose: () => void;
}

/**
 * Moves a file or folder to another folder on the same shelf, keeping its name: the reader walks the
 * shelf's folders and picks one. A folder cannot go into itself, so it and what is under it are left out.
 */
export function MoveDialog({ shelf, entry, onClose }: MoveDialogProps) {
  const moveEntry = useMoveEntry(shelf);
  const from = parentPath(entry.value.path);
  const [folder, setFolder] = useState(from);
  const { data: listing, isLoading } = useListing(shelf, folder);
  const { error, submit } = useSubmit('Could not move it');

  const isSelfOrInside = (path: string) =>
    entry.kind === 'directory' && (path === entry.value.path || path.startsWith(`${entry.value.path}/`));
  const folders = (listing?.directories ?? []).filter((directory) => !isSelfOrInside(directory.path));

  function move(): Promise<void> {
    return submit(() => moveEntry.mutateAsync({ kind: entry.kind, id: entry.value.id, toPath: joinPath(folder, entry.value.name) }), onClose);
  }

  return (
    <FormDialog
      open
      title={`Move ${entry.value.name}`}
      onClose={onClose}
      onSubmit={() => void move()}
      submitDisabled={folder === from}
      submitLabel="Move here"
      submitting={moveEntry.isPending}
      error={error}
    >
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
          <button
            key={directory.id}
            type="button"
            onClick={() => setFolder(directory.path)}
            className="flex w-full items-center gap-3 px-3 py-2 text-left text-body-md text-fg hover:bg-row-hover"
          >
            <Icon icon={iconMap.folder} size={16} className="shrink-0 text-muted" />
            <span className="min-w-0 flex-1 truncate">{directory.name}</span>
            <Icon icon={iconMap.chevronRight} size={14} className="shrink-0 text-neutral-9" />
          </button>
        ))}
      </div>

      <p className="text-body-sm text-muted">
        {folder === from ? 'It is in this folder now: pick another.' : 'Nothing already there is replaced.'}
      </p>
    </FormDialog>
  );
}
