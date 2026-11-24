import { DEFAULT_WORK_FACTOR } from '../lib/encryption/age';
import type { AskPassword } from '../lib/encryption/askPassword';
import type { OpenFolder } from '../lib/openFolder';
import { joinPath } from '../lib/shelfPaths';
import { ApiError } from './apiClient';
import { ERROR_CODES } from './errorCodes';
import { fileStoring } from './fileStoring';
import { shelfPasswords } from './shelfPasswords';
import { fetchFileAt } from './shelfService';

export interface Upload {
  id: number;
  file: File;
  path: string;
}

/** Where an upload is: told to the reporter each time it changes. */
export type UploadState =
  | { status: 'uploading'; sentBytes: number }
  | { status: 'done' }
  /** There is a file at its path; `failure` is why replacing it, once that was tried, failed. */
  | { status: 'taken'; failure?: unknown; replace: () => void }
  | { status: 'failed'; error: unknown }
  /** A password it needs was not given. */
  | { status: 'cancelled' };

export type UploadReporter = (upload: Upload, state: UploadState) => void;

/** What uploads go through. */
export interface UploadQueueAccess {
  /** Asks for an encrypted folder's password before anything is queued; its prompt shows beside that of `ask`. */
  askFirst: AskPassword;
  /** Asks for a password an upload turns out to need in its turn. */
  ask: AskPassword;
  report: UploadReporter;
  /** Told once a file is stored. */
  changed: () => void;
  /** What an encrypted upload is encrypted at; only tests lower it. */
  workFactor?: number;
}

/**
 * Queues `files` to be uploaded into `folder`, as it was read where this was started, each under its own name.
 * An encrypted folder's password is asked for first, and none is queued when it is not given.
 */
export type QueueUploads = (shelf: string, folder: OpenFolder, files: File[]) => Promise<void>;

/**
 * A queue that uploads files one at a time and reports how each one goes, storing each as `StoreFile` says. A
 * password not given ends those of the same call that wait, so it is asked for once. A file whose name is taken
 * is offered for replacing, and again after that was cancelled or failed; none is ever overwritten unasked.
 */
export function uploadQueue({ askFirst, ask, report, changed, workFactor = DEFAULT_WORK_FACTOR }: UploadQueueAccess): QueueUploads {
  let lastId = 0;
  let lastTurn = Promise.resolve();

  return async (shelf, folder, files) => {
    if (folder.encryptedBy && (await shelfPasswords(shelf, askFirst).folderPassword(folder.encryptedBy)) === null) return;

    const store = fileStoring(shelf, shelfPasswords(shelf, ask), workFactor);
    /** The turns of these files that wait; one taken out ends as it comes. */
    const waiting = new Set<object>();

    function enqueue(upload: Upload, overwrite: boolean): void {
      const turn = {};
      waiting.add(turn);
      lastTurn = lastTurn.then(async () => {
        if (waiting.delete(turn)) await send(upload, overwrite);
        else reportCancelled(upload, overwrite);
      });
    }

    async function send(upload: Upload, overwrite: boolean): Promise<void> {
      report(upload, { status: 'uploading', sentBytes: 0 });
      try {
        const stored = await store(folder, upload.path, upload.file, {
          overwrite,
          onProgress: (sentBytes) => report(upload, { status: 'uploading', sentBytes }),
        });
        if (!stored) {
          waiting.clear();
          reportCancelled(upload, overwrite);
          return;
        }
        report(upload, { status: 'done' });
        changed();
      } catch (error) {
        if (overwrite) offerReplacing(upload, error);
        else if (await isNameTaken(upload, error)) offerReplacing(upload);
        else report(upload, { status: 'failed', error });
      }
    }

    function offerReplacing(upload: Upload, failure?: unknown): void {
      report(upload, { status: 'taken', failure, replace: () => enqueue(upload, true) });
    }

    function reportCancelled(upload: Upload, overwrite: boolean): void {
      if (overwrite) offerReplacing(upload);
      else report(upload, { status: 'cancelled' });
    }

    /** Whether an upload was refused for a file at its path: the server answers a conflict for more than that. */
    async function isNameTaken(upload: Upload, refusal: unknown): Promise<boolean> {
      if (!(refusal instanceof ApiError) || refusal.code !== ERROR_CODES.conflict) return false;
      // Not known, it is the refusal that is said.
      return (await fetchFileAt(shelf, upload.path).catch(() => null)) !== null;
    }

    for (const file of files) enqueue({ id: ++lastId, file, path: joinPath(folder.directory.path, file.name) }, false);
  };
}
