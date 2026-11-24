import { useMemo } from 'react';
import type { PagePreview } from '../lib/preview/shownPreview';
import { isNotFound } from '../services/apiClient';
import { deletedFile, type Decryption } from '../services/fileReading';
import { useCloseWhenGone, usePreviewParam } from './usePreviewParam';
import { useDeletion } from './useShelfQueries';

export interface TrashPreview extends PagePreview {
  /** Opens the preview of the file a file deletion took. */
  open: (deletionId: string) => void;
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

  return { isOpen: id !== null && !gone, open, shown: { previewed, error, steps: null, onClose: close, asksPassword: wasOpened } };
}
