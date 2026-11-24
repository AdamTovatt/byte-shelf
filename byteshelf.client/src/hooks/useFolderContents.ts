import { useEffect, useRef, useState } from 'react';
import { folderContents, type FolderAccess } from '../lib/plugins/folderContents';
import type { FolderContents } from '../lib/plugins/folderType';
import type { ShelfFile } from '../services/shelfService';

/** The contents of a folder for its viewer, which is of that folder (`folderKey`) alone: closed as it goes, null until they are there. */
export function useFolderContents({ shelf, folder, reading, store, changed, open }: FolderAccess): FolderContents | null {
  // As it was first, rather than as each listing of it answers it anew.
  const [held] = useState(folder);

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
