import { useCallback, useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';
import type { EncryptedFolder } from '../services/encryptionService';
import { folderKey, type OpenFolder } from '../lib/openFolder';
import type { ShelfPasswords } from '../services/shelfPasswords';

/** A folder whose viewer waits for its password: asked for as it opens, not given, asked for again, or given. */
export type FolderLock = 'opening' | 'locked' | 'unlocking' | 'unlocked';

interface Held {
  key: string | null;
  encryptedBy: EncryptedFolder | null;
  lock: FolderLock;
}

/** A folder as it opens, before anything is known of its password. */
function heldAtFirst(key: string | null, encryptedBy: EncryptedFolder | null): Held {
  return { key, encryptedBy, lock: encryptedBy ? 'opening' : 'unlocked' };
}

/**
 * Whether the open folder is unlocked: the password kept for the encrypted folder it is, or is in, passes its
 * check, or the one asked for as it opens does; or it is not encrypted. `folder` is null for one not asked for
 * now, which takes back what was asked and forgets what was answered.
 */
export function useFolderLock(folder: OpenFolder | null, folderPassword: ShelfPasswords['folderPassword']): { lock: FolderLock; unlock: () => void } {
  const key = folder && folderKey(folder);
  const encryptedBy = folder?.encryptedBy ?? null;
  const [held, setHeld] = useState(() => heldAtFirst(key, encryptedBy));
  if (held.key !== key) setHeld(heldAtFirst(key, encryptedBy));

  useEffect(() => {
    const { encryptedBy: asked, lock } = held;
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
