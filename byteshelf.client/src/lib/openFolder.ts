import type { EncryptedFolder } from '../services/encryptionService';
import type { DirectoryListing, ShelfDirectory } from '../services/shelfService';

/** A folder as it was read where it was opened. */
export interface OpenFolder {
  directory: ShelfDirectory;
  /** The encrypted folder it is, or is in; null for none. */
  encryptedBy: EncryptedFolder | null;
}

/** The folder a listing was read of. */
export function listedFolder({ directory, encryptedBy }: DirectoryListing): OpenFolder {
  return { directory, encryptedBy: encryptedBy ?? null };
}

/** Tells an open folder from another one, and from itself at another path or encrypted otherwise. */
export function folderKey({ directory, encryptedBy }: OpenFolder): string {
  return [directory.id, directory.path, encryptedBy?.directory.id, encryptedBy?.check].join('\n');
}
