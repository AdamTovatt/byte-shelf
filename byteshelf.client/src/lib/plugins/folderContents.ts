import type { FileReading } from '../../services/fileReading';
import type { StoreFile, StoreFolder } from '../../services/fileStoring';
import { deleteFile, fetchTree, moveFile, requireDirectoryAt, type ShelfFile } from '../../services/shelfService';
import { lockableReader, type LockableReader } from '../content/lockableReader';
import { eitherAborts, noPasswordGiven } from '../abort';
import { queryKeys } from '../queryKeys';
import { joinPath } from '../shelfPaths';
import type { FolderContents } from './folderType';

/** What a folder's contents go through. */
export interface FolderAccess {
  shelf: string;
  folder: StoreFolder;
  reading: FileReading;
  store: StoreFile;
  /** Told once something changed. */
  changed: () => void;
  open: (file: ShelfFile) => void;
}

/** A folder's contents as what holds them closes them, for good: what is being read or asked for ends, what is being changed goes on, and nothing more is done. */
export interface ClosableFolderContents extends FolderContents {
  close: () => void;
}

/** The contents of a folder, for its viewer. */
export function folderContents({ shelf, folder, reading, store, changed, open }: FolderAccess): ClosableFolderContents {
  const { directory } = folder;
  const leaving = new AbortController();
  const readers = new Set<LockableReader>();

  /** Where `path` from the folder is on the shelf. */
  function at(path: string): string {
    // Joined, it would name the folder itself.
    if (path === '') throw new Error('A file in the folder has a name.');
    return joinPath(directory.path, path);
  }

  async function changing<T>(change: () => Promise<T>): Promise<T> {
    leaving.signal.throwIfAborted();
    const answer = await change();
    changed();
    return answer;
  }

  return {
    queryKey: queryKeys.shelfContents.tree(shelf, directory.path),
    list: (after, signal) => fetchTree(shelf, directory.path, { after, signal: eitherAborts(leaving.signal, signal) }),
    read: (file) => {
      const reader = lockableReader({ file, reading });
      function close(): void {
        readers.delete(reader);
        reader.close();
      }
      if (leaving.signal.aborted) reader.close();
      else readers.add(reader);
      return { ...reader, close };
    },
    store: (path, contents, signal) => changing(async () => {
      const stored = await store(folder, at(path), contents, { asking: leaving.signal, storing: signal });
      if (!stored) throw noPasswordGiven();
      return stored;
    }),
    delete: async (file) => void await changing(() => deleteFile(shelf, file.id)),
    move: (file, path) => changing(async () => {
      const toPath = at(path);
      await requireDirectoryAt(shelf, directory);
      return moveFile(shelf, file.id, toPath);
    }),
    open: (file) => {
      if (!leaving.signal.aborted) open(file);
    },
    close: () => {
      leaving.abort();
      readers.forEach((reader) => reader.close());
      readers.clear();
    },
  };
}
