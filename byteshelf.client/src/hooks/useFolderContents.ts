import { useEffect, useRef, useState } from 'react';
import { folderContents, type FolderAccess } from '../lib/plugins/folderContents';
import type { FolderContents } from '../lib/plugins/folderType';
import type { StoreFolder } from '../services/fileStoring';
import type { ShelfFile } from '../services/shelfService';

/** Tells one folder's contents from another's: what a store into it goes by. */
function keyOf({ directory, encryptedBy }: StoreFolder): string {
  return [directory.id, directory.path, encryptedBy?.directory.id].join('\n');
}

/** The contents of a folder for its viewer, closed as that goes, and others once the folder is another or encrypted otherwise; null until they are there. */
export function useFolderContents({ shelf, folder, reading, store, changed, open }: FolderAccess): FolderContents | null {
  // As it was last another folder, rather than as each listing of it answers it anew.
  const [held, setHeld] = useState(folder);
  if (keyOf(held) !== keyOf(folder)) setHeld(folder);

  const latestOpen = useRef(open);
  useEffect(() => {
    latestOpen.current = open;
  });

  const [contents, setContents] = useState<FolderContents | null>(null);
  useEffect(() => {
    const made = folderContents({ shelf, folder: held, reading, store, changed, open: (file: ShelfFile) => latestOpen.current(file) });
    setContents(made);
    return () => made.close();
  }, [shelf, held, reading, store, changed]);

  return contents;
}
