import { useMemo } from 'react';
import { liveContent } from '../services/contentService';
import { fileReading, type FileReading } from '../services/fileReading';
import { useEncryption } from './useEncryption';
import { useFileActions, type FileActions } from './useFileActions';
import { useUploads } from './useUploads';

/** What goes on with a shelf's live files as one folder after another is shown, in whatever way. */
export interface ShelfWork {
  encryption: ReturnType<typeof useEncryption>;
  reading: FileReading;
  uploads: ReturnType<typeof useUploads>;
  fileActions: FileActions;
}

export function useShelfWork(shelf: string): ShelfWork {
  const encryption = useEncryption(shelf);
  const { decryption } = encryption;
  const reading = useMemo(() => fileReading(liveContent(shelf), decryption), [shelf, decryption]);
  return { encryption, reading, uploads: useUploads(shelf), fileActions: useFileActions(shelf) };
}
