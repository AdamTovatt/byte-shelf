import { useCallback, useEffect, useRef, useState } from 'react';
import { sharedLoad } from '../lib/sharedLoad';
import { runOrToast } from '../lib/utils';
import type { FileReading, ReadFile } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { useLoaded } from './useLoaded';

/** A file up to this size is read into the page at once, a larger one only when asked. */
const READ_UNASKED_BYTES = 100 * 1024 * 1024;

/** A file's whole contents, on their way into the page. */
export type WholeContents =
  | { status: 'reading' }
  | { status: 'read'; contents: Blob }
  /** Large enough that reading it into the page waits to be asked. */
  | { status: 'waiting'; read: () => void }
  /** Encrypted, and no password was given. */
  | { status: 'locked'; unlock: () => void }
  | { status: 'failed'; error: unknown; retry: () => void };

/** Tells one file's contents from another's: the same file renamed or described keeps them. */
export function contentsKey(file: ShelfFile): string {
  return `${file.id} ${file.sha256}`;
}

/**
 * The whole contents of `shown`, read into the page once; nothing is read while it is null. The `reading`
 * answered with them is its own, sharing what is read and asked for here: a file saved or opened through
 * it is read and unlocked once for both, and shows. What is read, or asked for, is dropped once another
 * file is shown or none is, unless it is wanted to save or open the file. A locked file's password is
 * asked for while `asksPassword` says so, and when unlocking it.
 */
export function useWholeContents(shown: ReadFile | null, asksPassword: () => boolean): { whole: WholeContents; reading: FileReading | null } {
  const key = shown && contentsKey(shown.file);
  const [asked, setAsked] = useState<string | null>(null);
  const waiting = shown !== null && shown.file.sizeBytes > READ_UNASKED_BYTES && asked !== key;

  const [joinRead] = useState(() => sharedLoad<Blob>());
  const [joinUnlock] = useState(() => sharedLoad<true>());
  const { reload, ...loaded } = useLoaded(waiting ? null : key, (signal) => readOnce(shown!, signal));

  function readOnce({ file, reading }: ReadFile, signal?: AbortSignal): Promise<Blob | null> {
    return joinRead(contentsKey(file), (until) => reading.read(file, until), signal);
  }

  /** Unlocks a file, shown then. */
  async function unlockOnce({ file, reading }: ReadFile, signal?: AbortSignal): Promise<boolean> {
    const unlocked = await joinUnlock(contentsKey(file), async (until) => (await reading.unlock(file, until)) || null, signal);
    if (unlocked) reload();
    return unlocked ?? false;
  }

  /** Starts reading what is shown, when it waits to be asked or failed. */
  function start(): void {
    setAsked(key);
    if (loaded.status === 'failed') reload();
  }

  const locked = loaded.status === 'done' && loaded.value === null;
  const askToUnlock = useUnlocking(locked ? key : null, (signal) => unlockOnce(shown!, signal), asksPassword);

  return {
    whole: whole(),
    reading: shown && {
      source: shown.reading.source,
      read: (file, signal) => {
        start();
        return readOnce({ ...shown, file }, signal);
      },
      unlock: (file, signal) => unlockOnce({ ...shown, file }, signal),
    },
  };

  function whole(): WholeContents {
    if (waiting) return { status: 'waiting', read: start };
    if (loaded.status === 'failed') return { status: 'failed', error: loaded.error, retry: start };
    if (loaded.status === 'loading') return { status: 'reading' };
    return loaded.value ? { status: 'read', contents: loaded.value } : { status: 'locked', unlock: askToUnlock };
  }
}

/**
 * Unlocks what is locked: asked for at once when `asksPassword` says so, and by what is answered here,
 * until something else is locked, or nothing.
 */
function useUnlocking(locked: string | null, unlock: (signal: AbortSignal) => Promise<unknown>, asksPassword: () => boolean): () => void {
  const asking = useRef<AbortController | null>(null);
  const latest = useRef({ unlock, asksPassword });
  useEffect(() => {
    latest.current = { unlock, asksPassword };
  });

  const ask = useCallback(() => {
    asking.current?.abort();
    const { signal } = (asking.current = new AbortController());
    void runOrToast(() => latest.current.unlock(signal), 'Could not unlock the file');
  }, []);

  useEffect(() => {
    if (locked === null) return;
    if (latest.current.asksPassword()) ask();
    return () => asking.current?.abort();
  }, [locked, ask]);

  return ask;
}
