import { useMemo } from 'react';
import { liveContent } from '../services/contentService';
import { fileReading, type FileReading } from '../services/fileReading';
import { fileStoring, type StoreFile } from '../services/fileStoring';
import type { ShelfPasswords } from '../services/shelfPasswords';
import { useEncryption, type Encryption } from './useEncryption';
import { useFileActions, type FileActions } from './useFileActions';
import { useShelfPasswords } from './useShelfPasswords';
import { useUploads } from './useUploads';

/** What goes on with a shelf's live files as one folder after another is shown, in whatever way. */
export interface ShelfWork {
  passwords: ShelfPasswords;
  encryption: Encryption;
  reading: FileReading;
  store: StoreFile;
  uploads: ReturnType<typeof useUploads>;
  fileActions: FileActions;
}

export function useShelfWork(shelf: string): ShelfWork {
  const passwords = useShelfPasswords(shelf);
  const reading = useMemo(() => fileReading(liveContent(shelf), passwords.decryption), [shelf, passwords]);
  const store = useMemo(() => fileStoring(shelf, passwords), [shelf, passwords]);
  return { passwords, reading, store, encryption: useEncryption(shelf, passwords, reading), uploads: useUploads(shelf), fileActions: useFileActions(shelf) };
}
