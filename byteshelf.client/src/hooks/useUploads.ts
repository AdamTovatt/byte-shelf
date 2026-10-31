import { useCallback, useRef, useState } from 'react';
import { ApiError } from '../services/apiClient';
import { ERROR_CODES } from '../services/errorCodes';
import { uploadFile } from '../services/uploadService';
import { joinPath } from '../lib/shelfPaths';
import { getErrorMessage } from '../lib/utils';
import { useInvalidateShelf } from './useShelfQueries';

export type UploadStatus = 'waiting' | 'uploading' | 'done' | 'failed' | 'taken';

export interface UploadItem {
  id: number;
  file: File;
  path: string;
  sentBytes: number;
  status: UploadStatus;
  error?: string;
}

/**
 * Uploads files to a shelf one at a time, keeping each one's progress. A file whose name is taken
 * waits as 'taken' until it is replaced or dismissed; nothing is ever overwritten unasked.
 */
export function useUploads(shelf: string) {
  const [items, setItems] = useState<UploadItem[]>([]);
  const nextId = useRef(1);
  const queue = useRef<Promise<void>>(Promise.resolve());
  const invalidate = useInvalidateShelf();

  const update = useCallback((id: number, change: Partial<UploadItem>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...change } : item)));
  }, []);

  const run = useCallback((item: UploadItem, overwrite: boolean) => {
    queue.current = queue.current.then(async () => {
      update(item.id, { status: 'uploading', sentBytes: 0, error: undefined });
      try {
        await uploadFile({
          shelf,
          path: item.path,
          file: item.file,
          overwrite,
          onProgress: (sentBytes) => update(item.id, { sentBytes }),
        });
        update(item.id, { status: 'done', sentBytes: item.file.size });
        invalidate();
      } catch (caught) {
        const taken = caught instanceof ApiError && caught.code === ERROR_CODES.conflict && !overwrite;
        update(item.id, { status: taken ? 'taken' : 'failed', error: getErrorMessage(caught, 'The upload failed') });
      }
    });
  }, [shelf, update, invalidate]);

  /** Uploads `files` into the directory at `directory`, each under its own name. */
  const upload = useCallback((files: File[], directory: string) => {
    const added = files.map((file): UploadItem => ({
      id: nextId.current++,
      file,
      path: joinPath(directory, file.name),
      sentBytes: 0,
      status: 'waiting',
    }));
    setItems((current) => [...current, ...added]);
    for (const item of added) run(item, false);
  }, [run]);

  const replace = useCallback((item: UploadItem) => run(item, true), [run]);

  const dismiss = useCallback((id: number) => {
    setItems((current) => current.filter((item) => item.id !== id));
  }, []);

  const clearFinished = useCallback(() => {
    setItems((current) => current.filter((item) => item.status !== 'done'));
  }, []);

  return { items, upload, replace, dismiss, clearFinished };
}
