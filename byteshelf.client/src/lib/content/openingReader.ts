import { eitherAborts, untilAborted } from '../abort';
import type { ClosableReader } from './contentReader';

/** A reader that opens another as it is first read. */
export interface OpeningReader extends ClosableReader {
  /** Aborts as it is closed. */
  readonly closed: AbortSignal;
  /** Reads `reader` from now on, unless another is open. */
  take: (reader: ClosableReader) => void;
}

/**
 * Reads what `open` opens once something is read, once for all that reads it; when that failed, again.
 * Closing it is for good, and aborts `open`'s signal.
 */
export function openingReader(open: (signal: AbortSignal) => Promise<ClosableReader>): OpeningReader {
  const closing = new AbortController();
  let opening: Promise<ClosableReader> | null = null;

  function openOnce(): Promise<ClosableReader> {
    if (opening) return opening;
    const mine = open(closing.signal);
    opening = mine;
    mine.catch(() => {
      if (opening === mine) opening = null;
    });
    return mine;
  }

  /** Runs `read` on the contents, unless closed or aborted before they are there. */
  async function opened<T>(read: (reader: ClosableReader, until: AbortSignal) => Promise<T>, signal?: AbortSignal): Promise<T> {
    const until = eitherAborts(closing.signal, signal);
    until.throwIfAborted();
    return read(await untilAborted(openOnce(), until), until);
  }

  return {
    url: (signal) => opened((reader, until) => reader.url(until), signal),
    blob: (signal) => opened((reader, until) => reader.blob(until), signal),
    range: (start, end, signal) => opened((reader, until) => reader.range(start, end, until), signal),
    closed: closing.signal,
    take: (reader) => {
      opening = opening ? opening.catch(() => reader) : Promise.resolve(reader);
    },
    close: () => {
      closing.abort();
      void opening?.then((reader) => reader.close(), () => undefined);
    },
  };
}
