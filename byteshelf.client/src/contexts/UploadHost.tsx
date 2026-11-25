import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toastMissedUpload } from '../components/uploadToasts';
import { listStoredFile } from '../hooks/useShelfQueries';
import { useUploadQueue, type UploadFiles } from '../hooks/useUploadQueue';
import { folderRows, type FolderRow } from '../lib/uploadRows';
import { NO_UPLOADS, uploadStore, type HeldState, type UploadStore } from '../lib/uploadStore';
import type { DirectoryListing } from '../services/shelfService';
import type { UploadReporter } from '../services/uploadQueue';

interface Uploads {
  uploadFiles: UploadFiles;
  store: UploadStore;
}

const UploadContext = createContext<Uploads | null>(null);

/**
 * Holds the uploads of the pages under it, which it outlives: what is queued, the rows that show how each
 * goes, what a taken name offers and a password asked for stay as a page is left. One done is put into its
 * folder's listing, which its row then is. A password is asked for no longer once this is gone, as on signing
 * out.
 */
export function UploadHost({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [store] = useState(() => uploadStore(toastMissedUpload));
  const [report] = useState((): UploadReporter => (upload, state) => {
    if (state.status !== 'done') store.report(upload, state);
    else void listStoredFile(queryClient, upload.shelf, upload.folderPath, state.file, () => store.remove(upload.id));
  });
  const uploadFiles = useUploadQueue(report);
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

const NO_ROWS: readonly FolderRow[] = [];

/**
 * A folder's rows with the uploads into it, as `folderRows` says; none until it is listed. The uploads are those
 * filed under the path its listing has. While `shown`, the folder counts as shown, so one whose name is taken or
 * that fails is told of on its row alone.
 */
export function useFolderRows(shelf: string, listing: DirectoryListing | undefined, shown: boolean): readonly FolderRow[] {
  const { store } = useUploads();
  const path = listing?.directory.path ?? null;
  const uploads = useSyncExternalStore(store.subscribe, () => (path === null ? NO_UPLOADS : store.inFolder(shelf, path)));
  useEffect(() => (shown && path !== null ? store.show(shelf, path) : undefined), [store, shelf, path, shown]);

  return useMemo(() => (listing ? folderRows(listing, uploads) : NO_ROWS), [listing, uploads]);
}

/** Where an upload is, null once it is gone, and the way to dismiss it. */
export function useUpload(id: number): { state: HeldState | null; dismiss: () => void } {
  const { store } = useUploads();
  const state = useSyncExternalStore(store.subscribe, () => store.stateOf(id));
  return { state, dismiss: () => store.remove(id) };
}
