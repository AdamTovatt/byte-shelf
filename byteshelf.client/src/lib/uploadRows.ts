import type { Upload } from '../services/uploadQueue';
import type { Entry, ShelfFile } from '../services/shelfService';
import { compareNames } from './shelfPaths';
import type { ListedUpload } from './uploadStore';

/** A row of a folder's files: a file, or an upload on its way into the folder or not stored after all. */
export type FileRow = Extract<Entry, { kind: 'file' }> | { kind: 'upload'; value: Upload };

/** A folder's files as its listing has them. */
export interface ListedFiles {
  files: readonly ShelfFile[];
  /** When the listing was read. */
  readAt: number;
}

/** A folder's file rows, and the uploads done that its listing has caught up with, to be taken away. */
export interface UploadRows {
  rows: FileRow[];
  released: readonly ListedUpload[];
}

/**
 * A folder's files as listed, in the listing's order, by name, with the uploads into it among them, so a row
 * never moves as its file takes its place. One done is the file it was stored as, and one that replaces a file,
 * or is done, is in the place of that file's row: there are never two rows of one name but for a name taken.
 *
 * One done is released once the listing has its file, or was read after it was done, for a file gone since,
 * deleted or moved: what reads the listing again as one is done ends any reading begun before.
 */
export function withUploads({ files, readAt }: ListedFiles, uploads: readonly ListedUpload[]): UploadRows {
  const listed = new Set(files.map(({ id }) => id));
  const isReleased = ({ stored }: ListedUpload) => stored !== null && (listed.has(stored.file.id) || readAt > stored.at);
  const released = uploads.filter(isReleased);
  const shown = uploads.filter((upload) => !isReleased(upload));
  const taken = new Set(shown.filter(({ replacing, stored }) => replacing || stored).map(({ upload }) => upload.name));
  const coming = shown
    .map(({ upload, stored }): FileRow => (stored ? { kind: 'file', value: stored.file } : { kind: 'upload', value: upload }))
    .sort((a, b) => compareNames(a.value.name, b.value.name));

  const rows: FileRow[] = [];
  let next = 0;
  for (const file of files) {
    if (taken.has(file.name)) continue;
    // One of a name taken comes after the file of that name.
    while (next < coming.length && compareNames(coming[next].value.name, file.name) < 0) rows.push(coming[next++]);
    rows.push({ kind: 'file', value: file });
  }
  rows.push(...coming.slice(next));
  return { rows, released };
}
