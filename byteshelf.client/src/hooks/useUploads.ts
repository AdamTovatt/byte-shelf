import { useMemo, useRef } from 'react';
import { ApiError } from '../services/apiClient';
import { ERROR_CODES } from '../services/errorCodes';
import { uploadFile } from '../services/uploadService';
import { joinPath } from '../lib/shelfPaths';
import { DEFAULT_WORK_FACTOR, encryptBlob } from '../lib/encryption/age';
import { getErrorMessage } from '../ui';
import { reportUpload } from '../components/uploadToasts';
import { useInvalidateShelf } from './useShelfQueries';

export interface Upload {
  id: number;
  file: File;
  path: string;
  /** Encrypts the file with this before it is sent, as a file in an encrypted folder must be. */
  password?: string;
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
 * `workFactor` is what an encrypted upload is encrypted at; only tests lower it.
 */
export function useUploads(shelf: string, report: UploadReporter = reportUpload, workFactor = DEFAULT_WORK_FACTOR) {
  const nextId = useRef(1);
  const queue = useRef<Promise<void>>(Promise.resolve());
  const invalidate = useInvalidateShelf();

  return useMemo(() => {
    // A declaration rather than a callback, so a taken name's Replace can queue the same upload again.
    function run(upload: Upload, overwrite: boolean): void {
      queue.current = queue.current.then(async () => {
        report(upload, { status: 'uploading', sentBytes: 0 });
        try {
          const { file, password } = upload;
          await uploadFile({
            shelf,
            path: upload.path,
            file: password === undefined ? file : await encryptBlob(file, password, file.type, workFactor),
            overwrite,
            encrypted: password !== undefined,
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
      /** Uploads `files` into the directory at `directory`, each under its own name, encrypted with `password` when one is given. */
      upload(files: File[], directory: string, password?: string): void {
        for (const file of files) run({ id: nextId.current++, file, path: joinPath(directory, file.name), password }, false);
      },
    };
  }, [shelf, report, workFactor, invalidate]);
}
