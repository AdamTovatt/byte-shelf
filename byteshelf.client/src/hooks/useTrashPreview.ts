import { useMemo } from 'react';
import { isNotFound } from '../services/apiClient';
import { deletedFile, type Decryption, type ReadFile } from '../services/contentService';
import { useCloseWhenGone, usePreviewParam } from './usePreviewParam';
import { useDeletion } from './useShelfQueries';

export interface TrashPreview {
  isOpen: boolean;
  /** Opens the preview of the file a file deletion took. */
  open: (deletionId: string) => void;
  /** What the preview shows while it is open. */
  shown: {
    previewed: ReadFile | null;
    error: Error | null;
    onClose: () => void;
    asksPassword: () => boolean;
  };
}

/**
 * The preview over a shelf's trash, of the one file a file deletion took, named in the URL by the
 * deletion. What a folder's deletion took is not previewed.
 */
export function useTrashPreview(shelf: string, decryption: Decryption): TrashPreview {
  const { id, open, close, wasOpened } = usePreviewParam();
  const { data: deletion, error, isFetching } = useDeletion(shelf, id);
  const previewed = useMemo(() => (deletion ? deletedFile(shelf, deletion, decryption) : null), [shelf, deletion, decryption]);

  const gone = !isFetching && (isNotFound(error) || (deletion !== undefined && !previewed));
  useCloseWhenGone(gone, close);

  return { isOpen: id !== null && !gone, open, shown: { previewed, error, onClose: close, asksPassword: wasOpened } };
}
