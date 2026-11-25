import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import { toastMissedUpload } from '../components/uploadToasts';
import { useUploadQueue, type UploadFiles } from '../hooks/useUploadQueue';
import { withUploads, type FileRow, type ListedFiles } from '../lib/uploadRows';
import { uploadStore, type ListedUpload, type UploadStore } from '../lib/uploadStore';
import type { UploadState } from '../services/uploadQueue';

interface Uploads {
  uploadFiles: UploadFiles;
  store: UploadStore;
}

const UploadContext = createContext<Uploads | null>(null);

/**
 * Holds the uploads of the pages under it, which it outlives: what is queued, the rows that show how each
 * goes, what a taken name offers and a password asked for stay as a page is left. A password is asked for no
 * longer once this is gone, as on signing out.
 */
export function UploadHost({ children }: { children: ReactNode }) {
  const [store] = useState(() => uploadStore(toastMissedUpload));
  const uploadFiles = useUploadQueue(store.report);
  const uploads = useMemo(() => ({ uploadFiles, store }), [uploadFiles, store]);
  return <UploadContext.Provider value={uploads}>{children}</UploadContext.Provider>;
}

function useUploads(): Uploads {
  const uploads = useContext(UploadContext);
  if (!uploads) throw new Error('Uploads must be used within an UploadHost');
  return uploads;
}

/** Uploads files, going on as the page that does is left. */
export function useUploadFiles(): UploadFiles {
  return useUploads().uploadFiles;
}

const NO_UPLOADS: readonly ListedUpload[] = [];

/** A folder as its listing was read: the uploads into it are those filed under its path. */
export interface ListedFolder extends ListedFiles {
  path: string;
}

/**
 * The rows of a folder's files with the uploads into it, as `withUploads` says, taking away each done that its
 * listing has caught up with; none until it is listed. While `shown`, the folder counts as shown, so one whose
 * name is taken or that fails is told of on its row alone.
 */
export function useUploadRows(shelf: string, folder: ListedFolder | null, shown: boolean): FileRow[] {
  const { store } = useUploads();
  const path = folder?.path ?? null;
  const uploads = useSyncExternalStore(store.subscribe, () => (path === null ? NO_UPLOADS : store.inFolder(shelf, path)));
  useEffect(() => (shown && path !== null ? store.show(shelf, path) : undefined), [store, shelf, path, shown]);

  const files = folder?.files;
  const readAt = folder?.readAt ?? 0;
  const { rows, released } = useMemo(() => withUploads({ files: files ?? [], readAt }, uploads), [files, readAt, uploads]);
  useEffect(() => {
    for (const { upload } of released) store.remove(upload.id);
  }, [store, released]);
  return rows;
}

/** Where an upload is, null once it is gone, and the way to dismiss it. */
export function useUpload(id: number): { state: UploadState | null; dismiss: () => void } {
  const { store } = useUploads();
  const state = useSyncExternalStore(store.subscribe, () => store.stateOf(id));
  return { state, dismiss: () => store.remove(id) };
}
