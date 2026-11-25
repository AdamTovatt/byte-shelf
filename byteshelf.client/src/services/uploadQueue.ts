import { untilAborted } from '../lib/abort';
import { DEFAULT_WORK_FACTOR } from '../lib/encryption/age';
import type { AskPassword } from '../lib/encryption/askPassword';
import type { OpenFolder } from '../lib/openFolder';
import { joinPath } from '../lib/shelfPaths';
import { ApiError } from './apiClient';
import { ERROR_CODES } from './errorCodes';
import { fileStoring } from './fileStoring';
import { shelfPasswords } from './shelfPasswords';
import { fetchFileAt, type ShelfFile } from './shelfService';

export interface Upload {
  id: number;
  shelf: string;
  /** The path of the folder it goes into. */
  folderPath: string;
  file: File;
  /** The name it is stored under: its file's, composed as the server stores names, so it compares with theirs. */
  name: string;
  /** Where it is stored: in its folder, under its name. */
  path: string;
}

/** What an upload on its way has: whether it goes over the file at its path, and the way to end it. */
interface Underway {
  replacing: boolean;
  /** Ends it, sending nothing more; it is then told as cancelled. */
  cancel: () => void;
}

/** Where an upload on its way is. */
export type UnderwayState =
  /** Queued behind others, or looking up how it is stored. */
  | ({ status: 'waiting' } & Underway)
  /** Encrypted with its folder's or file's password, before anything is sent. */
  | ({ status: 'encrypting' } & Underway)
  /** `totalBytes` is how many it sends: encrypted, as many as that makes. Once all are sent, it waits for the server's answer. */
  | ({ status: 'uploading'; sentBytes: number; totalBytes: number } & Underway);

/** Where an upload that went wrong is: its name was taken, or it failed. */
export type MissedState =
  /** There is a file at its path; `failure` is why replacing it, once that was tried, failed. */
  | { status: 'taken'; failure?: unknown; replace: () => void }
  | { status: 'failed'; error: unknown };

/** Where an upload is: told to the reporter each time it changes. */
export type UploadState =
  | UnderwayState
  | { status: 'done'; file: ShelfFile }
  | MissedState
  /** Cancelled, or a password it needs was not given. */
  | { status: 'cancelled' };

/** Whether an upload is on its way: waiting, encrypting or being sent. */
export function isUnderway(state: UploadState): state is UnderwayState {
  return state.status === 'waiting' || state.status === 'encrypting' || state.status === 'uploading';
}

/** Whether an upload went wrong: its name was taken, or it failed. */
export function isMissed(state: UploadState): state is MissedState {
  return state.status === 'taken' || state.status === 'failed';
}

export type UploadReporter = (upload: Upload, state: UploadState) => void;

/** What uploads go through. */
export interface UploadQueueAccess {
  /** Asks for an encrypted folder's password before anything is queued; its prompt shows beside that of `ask`. */
  askFirst: AskPassword;
  /** Asks for a password an upload turns out to need in its turn. */
  ask: AskPassword;
  /** Told how each upload goes; one done is told with the file it was stored as, for what shows the shelf to list. */
  report: UploadReporter;
  /** Told once a file may have been stored that is not told done: one cancelled once all of it was sent. */
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
 * is offered for replacing, and again after its password was not given or replacing it failed; none is ever
 * overwritten unasked. One cancelled is told so at once, and the next goes on: what it was encrypting is
 * finished behind it, and then nothing is sent.
 */
export function uploadQueue({ askFirst, ask, report, changed, workFactor = DEFAULT_WORK_FACTOR }: UploadQueueAccess): QueueUploads {
  let lastId = 0;
  let lastTurn = Promise.resolve();

  return async (shelf, folder, files) => {
    if (folder.encryptedBy && (await shelfPasswords(shelf, askFirst).folderPassword(folder.encryptedBy)) === null) return;

    const store = fileStoring(shelf, shelfPasswords(shelf, ask), workFactor);
    /** Ends each of these files that waits for its turn, as a password was not given. */
    const waiting = new Set<() => void>();

    function enqueue(upload: Upload, overwrite: boolean): void {
      const cancelling = new AbortController();
      const { signal } = cancelling;
      /** Tells how it goes, until it is ended. */
      const tell = (state: UploadState) => {
        if (!signal.aborted) report(upload, state);
      };
      const end = (state: UploadState) => {
        if (signal.aborted) return;
        cancelling.abort();
        report(upload, state);
      };
      const underway: Underway = { replacing: overwrite, cancel: () => end({ status: 'cancelled' }) };
      const passOver = () => end(notGiven(upload, overwrite));

      async function send(): Promise<void> {
        try {
          const stored = await store(folder, upload.path, upload.file, {
            overwrite,
            storing: signal,
            onEncrypting: () => tell({ status: 'encrypting', ...underway }),
            onProgress: (sentBytes, totalBytes) => tell({ status: 'uploading', sentBytes, totalBytes, ...underway }),
          });
          if (!stored) {
            for (const other of waiting) other();
            tell(notGiven(upload, overwrite));
            return;
          }
          tell({ status: 'done', file: stored });
        } catch (error) {
          if (signal.aborted) return;
          if (overwrite) tell(offerReplacing(upload, error));
          else if (await isNameTaken(upload, error)) tell(offerReplacing(upload));
          else tell({ status: 'failed', error });
        }
      }

      waiting.add(passOver);
      tell({ status: 'waiting', ...underway });
      lastTurn = lastTurn.then(async () => {
        waiting.delete(passOver);
        if (signal.aborted) return;
        // Waited for no longer once it is cancelled: what it does goes on, telling nothing. Not waited out,
        // though encrypting goes on until it is done, holding what it makes: one cancelled, often for being
        // slow, is not to hold back the rest, and what goes on at once is only as many as are cancelled then.
        // Cancelled once all was sent, it may be stored all the same, though not told done, whether or not its
        // answer came: what shows the shelf reads it again as it ends.
        const sent = send().finally(() => {
          if (signal.aborted) changed();
        });
        await untilAborted(sent, signal).catch(() => undefined);
      });
    }

    function offerReplacing(upload: Upload, failure?: unknown): UploadState {
      return { status: 'taken', failure, replace: () => enqueue(upload, true) };
    }

    /** What an upload whose password was not given ends as: what replaces a file is offered again. */
    function notGiven(upload: Upload, overwrite: boolean): UploadState {
      return overwrite ? offerReplacing(upload) : { status: 'cancelled' };
    }

    /** Whether an upload was refused for a file at its path: the server answers a conflict for more than that. */
    async function isNameTaken(upload: Upload, refusal: unknown): Promise<boolean> {
      if (!(refusal instanceof ApiError) || refusal.code !== ERROR_CODES.conflict) return false;
      // Not known, it is the refusal that is said.
      return (await fetchFileAt(shelf, upload.path).catch(() => null)) !== null;
    }

    const folderPath = folder.directory.path;
    for (const file of files) {
      const name = file.name.normalize('NFC');
      enqueue({ id: ++lastId, shelf, folderPath, file, name, path: joinPath(folderPath, name) }, false);
    }
  };
}
