import type { ComponentType } from 'react';
import type { IconComponent } from '../../ui';
import type { DirectoryTree, ShelfDirectory, ShelfFile } from '../../services/shelfService';
import type { LockableReader } from '../content/lockableReader';

/**
 * What is below a folder, as its viewer reads and changes it. Paths are from the folder (`small/beach.jpg`)
 * and name nothing outside it; a file is taken as it is given, and is to be one `list` or `store` answered.
 * What fails says so to the viewer alone. Once the viewer has gone, everything does, with an `AbortError`.
 */
export interface FolderContents {
  /**
   * What the key of a query of `list` starts with, for it to be asked again as anything on the shelf changes.
   * Of `list` alone: a query keeps its answer after the viewer is gone, so what is read of a file, which may
   * be decrypted, is for `useLoaded`.
   */
  readonly queryKey: readonly unknown[];
  /** The folders and files below the folder at any depth, a page at a time: the first, or the one after the cursor `after`. */
  list: (after?: string, signal?: AbortSignal) => Promise<DirectoryTree>;
  /** Reads a file, until its reader is closed or the viewer goes. */
  read: (file: ShelfFile) => LockableReader;
  /**
   * Stores a file, over the one there, which goes to the trash, whatever it holds by then. It is typed as
   * `contents` is, or by its name. It is encrypted with the password of its encrypted folder, or of the file
   * it is stored over, asked for when none kept opens that, and stored plain where there is neither, whatever
   * `contents` was made from. With no password given it fails with an `AbortError`, storing nothing, and so
   * it does as `signal` aborts. As the viewer goes, a password is no longer asked for, and what is being
   * stored goes on. When else it fails, and what must not be stored, is in the server's `Plugins.md`.
   */
  store: (path: string, contents: Blob, signal?: AbortSignal) => Promise<ShelfFile>;
  /** Moves a file to the trash. */
  delete: (file: ShelfFile) => Promise<void>;
  /** Moves or renames a file within the folder; never over another. */
  move: (file: ShelfFile, path: string) => Promise<ShelfFile>;
  /** Opens a file as the listing does: in the preview, alone, or in a new tab when nothing previews it. */
  open: (file: ShelfFile) => void;
}

/** What the viewer of a folder's type is given. */
export interface FolderViewerProps {
  /** The name of the shelf the folder is on. */
  shelf: string;
  folder: ShelfDirectory;
  /** What is below the folder: the same for as long as the viewer shows. */
  contents: FolderContents;
  /** Whether the person may store, delete and move files here; false until it is known. */
  canWrite: boolean;
}

/** Shows folders whose names end in its suffix, in place of their listing. */
export interface FolderType {
  /** What follows the last dot of such a name, in whatever case: at least two lowercase letters and digits. */
  suffix: string;
  /** What such a folder is called, such as `Album`. */
  label: string;
  /** Such a folder's, in a listing. */
  icon: IconComponent;
  /** Shows the folder, under its breadcrumbs; one that loads code as it is first shown is a `lazy` component. */
  component: ComponentType<FolderViewerProps>;
}
