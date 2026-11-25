import type { Upload } from '../services/uploadQueue';
import { directoryEntry, type DirectoryListing, type Entry } from '../services/shelfService';
import { compareNames } from './shelfPaths';
import type { ListedUpload } from './uploadStore';

/** A row of a folder: a folder or file in it, or an upload on its way into it or not stored after all. */
export type FolderRow = Entry | { kind: 'upload'; value: Upload };

/**
 * A folder's rows: its folders, then its files with the uploads into it among them, each part in order by name.
 * The app orders them itself, by the one compare it puts the uploads in with, so no row moves as an upload comes
 * or goes, or as the file it was stored as takes its place at the end of the cached listing. One that replaces a
 * file is in that file's place: there are never two rows of one name but for a name taken, whose upload comes
 * after the file.
 */
export function folderRows(listing: DirectoryListing, uploads: readonly ListedUpload[]): FolderRow[] {
  const byName = (a: FolderRow, b: FolderRow) => compareNames(a.value.name, b.value.name);
  const replaced = new Set(uploads.filter(({ replacing }) => replacing).map(({ upload }) => upload.name));
  const files: FolderRow[] = [
    ...listing.files.filter(({ name }) => !replaced.has(name)).map((value): FolderRow => ({ kind: 'file', value })),
    ...uploads.map(({ upload }): FolderRow => ({ kind: 'upload', value: upload })),
  ];
  // Stable sorts, so a file stays ahead of the upload whose name it took.
  return [...listing.directories.map(directoryEntry).sort(byName), ...files.sort(byName)];
}
