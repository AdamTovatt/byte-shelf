import type { EncryptedFolder } from '../services/encryptionService';
import type { ShelfDirectory } from '../services/shelfService';

/** A folder as it was read where it was opened. */
export interface OpenFolder {
  directory: ShelfDirectory;
  /** The encrypted folder it is, or is in; null for none. */
  encryptedBy: EncryptedFolder | null;
}

/** Tells an open folder from another one, and from itself at another path or encrypted otherwise. */
export function folderKey({ directory, encryptedBy }: OpenFolder): string {
  return [directory.id, directory.path, encryptedBy?.directory.id, encryptedBy?.check].join('\n');
}
