import { createContext, useContext, type ReactNode } from 'react';
import { useUploadQueue, type UploadFiles } from '../hooks/useUploadQueue';

const UploadContext = createContext<UploadFiles | null>(null);

/**
 * Holds the uploads of the pages under it, which it outlives: what is queued, what a taken name offers and a
 * password asked for stay as a page is left. A password is asked for no longer once this is gone, as on signing
 * out.
 */
export function UploadHost({ children }: { children: ReactNode }) {
  return <UploadContext.Provider value={useUploadQueue()}>{children}</UploadContext.Provider>;
}

/** Uploads files, going on as the page that does is left. */
export function useUploadFiles(): UploadFiles {
  const uploadFiles = useContext(UploadContext);
  if (!uploadFiles) throw new Error('useUploadFiles must be used within an UploadHost');
  return uploadFiles;
}
