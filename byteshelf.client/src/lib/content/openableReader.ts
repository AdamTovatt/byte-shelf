import { isBlobBacked } from '../../services/contentService';
import type { OpenableFile } from '../../services/fileReading';
import { eitherAborts, untilAborted } from '../abort';
import { blobReader, sourceReader, type ClosableReader } from './contentReader';

/**
 * Reads a file that nothing shows yet: a blob-backed one's whole contents once they are asked for, its
 * password then too, failing with an `AbortError` when none is given. They are read once for all that asks.
 * Closing it is for good.
 */
export function openableReader({ file, source, readWhole }: OpenableFile): ClosableReader {
  const closing = new AbortController();
  let opening: Promise<ClosableReader> | null = null;

  async function open(): Promise<ClosableReader> {
    if (!isBlobBacked(file)) return sourceReader(file.id, source);
    const whole = await readWhole(closing.signal);
    if (!whole) throw new DOMException('No password was given.', 'AbortError');
    return blobReader(whole);
  }

  /** Runs `read` on the contents, unless closed or aborted before they are there. */
  async function opened<T>(read: (reader: ClosableReader, until: AbortSignal) => Promise<T>, signal?: AbortSignal): Promise<T> {
    const until = eitherAborts(closing.signal, signal);
    until.throwIfAborted();
    // Failed, or with no password given, it may be asked again.
    opening ??= open().catch((caught: unknown) => {
      opening = null;
      throw caught;
    });
    return read(await untilAborted(opening, until), until);
  }

  return {
    url: (signal) => opened((reader, until) => reader.url(until), signal),
    blob: (signal) => opened((reader, until) => reader.blob(until), signal),
    range: (start, end, signal) => opened((reader, until) => reader.range(start, end, until), signal),
    close: () => {
      closing.abort();
      void opening?.then((reader) => reader.close(), () => undefined);
    },
  };
}
