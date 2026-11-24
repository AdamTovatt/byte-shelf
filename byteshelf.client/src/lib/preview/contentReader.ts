import type { ContentSource } from '../../services/contentService';
import { eitherAborts } from '../abort';
import { elementUrl, type ElementUrl } from '../localBlobs';

/**
 * A previewed file's contents, as a viewer reads them: always plain, wherever they come from. What is
 * still loading is aborted by `signal`, and when the preview closes or moves on.
 */
export interface ContentReader {
  /** A URL an `<img>`, `<video>`, `<audio>` or `<iframe>` loads the contents from, until the preview closes or moves on. */
  url: (signal?: AbortSignal) => Promise<string>;
  /** The whole contents. */
  blob: (signal?: AbortSignal) => Promise<Blob>;
  /** The contents from `start` up to, not including, `end`, or their end when that comes first. */
  range: (start: number, end: number, signal?: AbortSignal) => Promise<Blob>;
}

/** A reader the preview closes: aborts what is loading and releases its URL. Reading again after that starts afresh. */
export interface ClosableReader extends ContentReader {
  close: () => void;
}

/**
 * Reads a file that is not blob-backed, its id `id`, from `source`, by its address or with the session's
 * token. The address answers the same contents for good: a store over the file makes one of another id, and
 * contents are swapped in place only as encryption starts or ends, where one side is blob-backed.
 */
export function sourceReader(id: string, source: ContentSource): ClosableReader {
  let reading = new AbortController();
  const until = (signal?: AbortSignal) => eitherAborts(reading.signal, signal);

  return {
    url: async (signal) => {
      const aborted = until(signal);
      const url = await source.navigationUrl(id, false);
      aborted.throwIfAborted();
      return url;
    },
    blob: (signal) => source.read(id, until(signal)),
    range: (start, end, signal) => source.readRange(id, start, end, until(signal)),
    close: () => {
      reading.abort();
      reading = new AbortController();
    },
  };
}

/** Reads a blob-backed file from its whole `contents`, already read into the page. */
export function blobReader(contents: Blob): ClosableReader {
  let shown: Promise<ElementUrl> | null = null;

  return {
    url: async (signal) => {
      shown ??= elementUrl(contents);
      const mine = shown;
      const { url } = await mine;
      signal?.throwIfAborted();
      if (shown !== mine) throw new DOMException('The preview closed.', 'AbortError');
      return url;
    },
    blob: async (signal) => {
      signal?.throwIfAborted();
      return contents;
    },
    range: async (start, end, signal) => {
      signal?.throwIfAborted();
      return contents.slice(start, end);
    },
    close: () => {
      void shown?.then(({ release }) => release(), () => undefined);
      shown = null;
    },
  };
}
