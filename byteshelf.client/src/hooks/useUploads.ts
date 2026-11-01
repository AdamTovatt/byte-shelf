import { useMemo, useRef } from 'react';
import { ApiError } from '../services/apiClient';
import { ERROR_CODES } from '../services/errorCodes';
import { uploadFile } from '../services/uploadService';
import { joinPath } from '../lib/shelfPaths';
import { getErrorMessage } from '../ui';
import { reportUpload } from '../components/uploadToasts';
import { useInvalidateShelf } from './useShelfQueries';

export interface Upload {
  id: number;
  file: File;
  path: string;
}

/** Where an upload is: told to the reporter each time it changes. */
export type UploadState =
  | { status: 'uploading'; sentBytes: number }
  | { status: 'done' }
  | { status: 'taken'; error: string; replace: () => void }
  | { status: 'failed'; error: string };

export type UploadReporter = (upload: Upload, state: UploadState) => void;

/**
 * Uploads files to a shelf one at a time, telling `report` (toasts, by default) how each one goes. A
 * file whose name is taken is offered for replacing; nothing is ever overwritten unasked.
 */
export function useUploads(shelf: string, report: UploadReporter = reportUpload) {
  const nextId = useRef(1);
  const queue = useRef<Promise<void>>(Promise.resolve());
  const invalidate = useInvalidateShelf();

  return useMemo(() => {
    // A declaration rather than a callback, so a taken name's Replace can queue the same upload again.
    function run(upload: Upload, overwrite: boolean): void {
      queue.current = queue.current.then(async () => {
        report(upload, { status: 'uploading', sentBytes: 0 });
        try {
          await uploadFile({
            shelf,
            path: upload.path,
            file: upload.file,
            overwrite,
            onProgress: (sentBytes) => report(upload, { status: 'uploading', sentBytes }),
          });
          report(upload, { status: 'done' });
          invalidate();
        } catch (caught) {
          const error = getErrorMessage(caught, 'The upload failed');
          if (caught instanceof ApiError && caught.code === ERROR_CODES.conflict && !overwrite) {
            report(upload, { status: 'taken', error, replace: () => run(upload, true) });
          } else {
            report(upload, { status: 'failed', error });
          }
        }
      });
    }

    return {
      /** Uploads `files` into the directory at `directory`, each under its own name. */
      upload(files: File[], directory: string): void {
        for (const file of files) run({ id: nextId.current++, file, path: joinPath(directory, file.name) }, false);
      },
    };
  }, [shelf, report, invalidate]);
}
