import { useCallback, useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';
import type { EncryptedFolder } from '../services/encryptionService';

/** A folder whose viewer waits for its password: asked for as it opens, not given, asked for again, or given. */
export type FolderLock = 'opening' | 'locked' | 'unlocking' | 'unlocked';

/** Asks for an encrypted folder's password, where none kept passes its check. Null when cancelled, as `signal` cancels it. */
export type AskFolderPassword = (folder: EncryptedFolder, signal?: AbortSignal) => Promise<string | null>;

interface Held {
  key: string | null;
  folder: EncryptedFolder | null;
  lock: FolderLock;
}

/** A folder as it opens, before anything is known of its password. */
function heldAtFirst(key: string | null, folder: EncryptedFolder | null): Held {
  return { key, folder, lock: folder ? 'opening' : 'unlocked' };
}

/**
 * Whether the folder open at `path` is unlocked: the password kept for the encrypted folder it is, or is in,
 * passes its check, or the one asked for as it opens does. `folder` is null for a folder that is not
 * encrypted, or not asked for now, which takes back what was asked and forgets what was answered.
 */
export function useFolderLock(path: string, folder: EncryptedFolder | null, folderPassword: AskFolderPassword): { lock: FolderLock; unlock: () => void } {
  const key = folder && [path, folder.directory.id, folder.check].join('\n');
  const [held, setHeld] = useState(() => heldAtFirst(key, folder));
  if (held.key !== key) setHeld(heldAtFirst(key, folder));

  useEffect(() => {
    const { folder: asked, lock } = held;
    if (!asked || lock === 'locked' || lock === 'unlocked') return undefined;

    const leaving = new AbortController();
    folderPassword(asked, leaving.signal)
      .catch((caught: unknown) => {
        if (!leaving.signal.aborted) toast.error(getErrorMessage(caught, 'Could not unlock the folder'));
        return null;
      })
      .then((password) => {
        if (!leaving.signal.aborted) setHeld({ ...held, lock: password === null ? 'locked' : 'unlocked' });
      });
    return () => leaving.abort();
  }, [held, folderPassword]);

  const unlock = useCallback(() => setHeld((now) => (now.lock === 'locked' ? { ...now, lock: 'unlocking' } : now)), []);
  return { lock: held.lock, unlock };
}
