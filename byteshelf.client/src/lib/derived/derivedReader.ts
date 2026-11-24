import { isNotFound, notReadyFor } from '../../services/apiClient';
import { opensByNavigation, type ContentSource } from '../../services/contentService';
import type { DerivedKind, ShelfFile } from '../../services/shelfService';
import { eitherAborts, sleep, untilAborted } from '../abort';
import { elementUrl, type ElementUrl } from '../localBlobs';

/** What plugins made from a file's contents, such as its thumbnail, as a viewer shows it until its preview closes or moves on. */
export interface DerivedReader {
  /** Whether the file of `kind` was there as the file was last listed, so that its URL is known at once. */
  has: (kind: DerivedKind) => boolean;
  /**
   * A URL an `<img>` loads the file of `kind` from, until the preview closes or moves on, which is waited for
   * while it is being made; null when there is none: nothing makes it of the file, it could not be made, or
   * the file is encrypted. Aborting `signal` ends the wait.
   */
  url: (kind: DerivedKind, signal?: AbortSignal) => Promise<string | null>;
  /** Has `url` ask for the file of `kind` anew, as when its URL did not load: what was listed may have gone since. */
  forget: (kind: DerivedKind) => void;
}

/** A reader as what holds it closes it, which ends what is loading and releases its URLs. Reading again after that starts afresh. */
export interface ClosableDerivedReader extends DerivedReader {
  /** Goes on with the file's latest record, of the same contents. */
  update: (file: ShelfFile) => void;
  close: () => void;
}

/** Reads what plugins made from `file`, answered by `source`. */
export function derivedReader(file: ShelfFile, source: ContentSource): ClosableDerivedReader {
  let latest = file;
  let reading = new AbortController();
  let urls = new Map<DerivedKind, Promise<string | null>>();
  const held: ElementUrl[] = [];
  /** The kinds whose listed URL did not load. */
  const gone = new Set<DerivedKind>();

  async function load(kind: DerivedKind, signal: AbortSignal): Promise<string | null> {
    if (latest.encrypted) return null;
    const derived = source.derived(latest.id, kind);
    if (opensByNavigation()) {
      const listed = gone.has(kind) ? undefined : latest.derived?.[kind];
      const version = listed === undefined ? await whenMade(() => derived.version(signal), signal) : `${latest.sha256}-${listed}`;
      return version === null ? null : derived.navigationUrl(version);
    }

    const blob = await whenMade(() => derived.read(signal), signal);
    if (!blob) return null;
    signal.throwIfAborted();
    const shown = await elementUrl(blob);
    if (signal.aborted) shown.release();
    signal.throwIfAborted();
    held.push(shown);
    return shown.url;
  }

  return {
    has: (kind) => !latest.encrypted && latest.derived?.[kind] !== undefined,
    url: (kind, signal) => {
      const until = eitherAborts(reading.signal, signal);
      let url = urls.get(kind);
      if (!url) {
        const loading = urls;
        const asked = load(kind, reading.signal);
        loading.set(kind, asked);
        const drop = () => loading.get(kind) === asked && loading.delete(kind);
        // Failed, or not there, it may be asked for again: a plugin may have been installed since.
        void asked.then((found) => found ?? drop(), drop);
        url = asked;
      }
      return untilAborted(url, until);
    },
    forget: (kind) => {
      gone.add(kind);
      urls.delete(kind);
    },
    update: (updated) => {
      latest = updated;
    },
    close: () => {
      reading.abort();
      reading = new AbortController();
      urls = new Map();
      gone.clear();
      held.splice(0).forEach(({ release }) => release());
    },
  };
}

/** What `ask` answers once the file has been made; null when there is none. */
async function whenMade<T>(ask: () => Promise<T>, signal: AbortSignal): Promise<T | null> {
  for (;;) {
    try {
      return await ask();
    } catch (error) {
      if (isNotFound(error)) return null;
      const seconds = notReadyFor(error);
      if (seconds === null) throw error;
      await sleep(seconds * 1000, signal);
    }
  }
}
