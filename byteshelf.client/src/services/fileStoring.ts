import { eitherAborts } from '../lib/abort';
import { DEFAULT_WORK_FACTOR, encryptBlob } from '../lib/encryption/age';
import { filePasswordKey, rememberPassword } from '../lib/encryption/passwords';
import { fetchEncryptedFolder } from './encryptionService';
import type { OpenFolder } from '../lib/openFolder';
import type { ShelfPasswords } from './shelfPasswords';
import { fetchFileAt, requireDirectoryAt, type ShelfFile } from './shelfService';
import { uploadFile } from './uploadService';

/** How a file is stored. */
export interface StoreOptions {
  /** Whether it may go over a file at its path, which then goes to the trash; one that may not is refused where there is one. */
  overwrite: boolean;
  /** Told how many bytes of it the server has, as it is sent. */
  onProgress?: (sentBytes: number) => void;
  /** Takes back asking for a password, no more. */
  asking?: AbortSignal;
  /** Ends it at any point, but for encrypting the contents, which is ended once that is done. */
  storing?: AbortSignal;
}

/**
 * Stores a file at `path`, which is below `folder`: encrypted with its encrypted folder's password, or over a
 * file with a password of its own with that one, and plain otherwise, each looked up as it is stored. Null when
 * the password was not given, with nothing stored.
 *
 * Nothing is sent, and it fails, when `folder` is no longer at its path, or its encryption was removed or is
 * now another folder's: what was to be stored encrypted is never sent plain. Encrypted since it was read, or
 * given another password, it is stored into as it is then. What is encrypted only once that was looked up is
 * not seen: into a folder the server then refuses the plain file, and over a file it goes as it is.
 */
export type StoreFile = (folder: OpenFolder, path: string, contents: Blob, options: StoreOptions) => Promise<ShelfFile | null>;

/** What a file is encrypted with as it is stored, and, for one with a password of its own, the file it is stored over. */
type StorePassword = { kind: 'none' } | { kind: 'cancelled' } | { kind: 'given'; password: string; over: ShelfFile | null };

/** Stores files on `shelf`, encrypted at `workFactor` with `passwords`. */
export function fileStoring(shelf: string, { folderPassword, filePassword }: Pick<ShelfPasswords, 'folderPassword' | 'filePassword'>, workFactor = DEFAULT_WORK_FACTOR): StoreFile {
  /** What a file stored at `path` is encrypted with, looked up and asked for when it is not kept. */
  async function storePassword(folder: OpenFolder, path: string, { overwrite, asking, storing }: StoreOptions): Promise<StorePassword> {
    const prompting = asking ? eitherAborts(asking, storing) : storing;
    const encryptedBy = await fetchEncryptedFolder(shelf, path, storing);
    if (folder.encryptedBy && folder.encryptedBy.directory.id !== encryptedBy?.directory.id) {
      throw new Error(`${folder.directory.name} is no longer encrypted as it was, or where it was.`);
    }
    if (encryptedBy) {
      const password = await folderPassword(encryptedBy, prompting);
      return password === null ? { kind: 'cancelled' } : { kind: 'given', password, over: null };
    }

    if (!overwrite) return { kind: 'none' };
    const over = await fetchFileAt(shelf, path, storing);
    if (!over?.encrypted) return { kind: 'none' };
    const password = await filePassword(over, prompting);
    return password === null ? { kind: 'cancelled' } : { kind: 'given', password, over };
  }

  return async (folder, path, contents, options) => {
    const { overwrite, onProgress, storing } = options;
    const sealing = await storePassword(folder, path, options);
    // A prompt taken back answers as one that was cancelled.
    storing?.throwIfAborted();
    if (sealing.kind === 'cancelled') return null;

    const encrypted = sealing.kind === 'given';
    const file = encrypted ? await encryptBlob(contents, sealing.password, contents.type, workFactor) : contents;
    // After what it is encrypted with was looked up by path, so that was this folder's.
    await requireDirectoryAt(shelf, folder.directory, storing);
    const stored = await uploadFile({ shelf, path, file, overwrite, encrypted, onProgress, signal: storing });
    // Stored over, the file there is another, with an id of its own.
    if (encrypted && sealing.over) rememberPassword(filePasswordKey(shelf, undefined, stored.id), sealing.password);
    return stored;
  };
}
