import type { EncryptedFolder } from './encryptionService';
import type { ShelfDirectory, ShelfFile } from './shelfService';

/** The folder files are stored in, as whoever stores them knows it. */
export interface StoreFolder {
  directory: ShelfDirectory;
  /** The encrypted folder it is, or is in; null for none. */
  encryptedBy: EncryptedFolder | null;
}

/** What ends storing a file. */
export interface StoreSignals {
  /** Takes back asking for a password, no more. */
  asking?: AbortSignal;
  /** Ends it at any point, but for encrypting the contents, which is ended once that is done. */
  storing?: AbortSignal;
}

/**
 * Stores a file at `path`, which is below `folder`, over the one there, which goes to the trash: encrypted
 * with its encrypted folder's password, or over a file with a password of its own with that one, and plain
 * otherwise. Null when the password was not given, with nothing stored.
 *
 * Nothing is sent, and it fails, when `folder` is no longer at its path, or no longer encrypted as it was:
 * what was to be stored encrypted is never sent plain. What is encrypted only once that was looked up is not
 * seen: into a folder the server then refuses the plain file, and over a file it goes as it is.
 */
export type StoreFile = (folder: StoreFolder, path: string, contents: Blob, signals?: StoreSignals) => Promise<ShelfFile | null>;
