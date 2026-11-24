import { isBlobBacked } from '../../services/contentService';
import type { OpenableFile } from '../../services/fileReading';
import { noPasswordGiven } from '../abort';
import { blobReader, sourceReader, type ClosableReader } from './contentReader';
import { openingReader } from './openingReader';

/**
 * Reads a file that nothing shows yet: a blob-backed one's whole contents once they are asked for, its
 * password then too, failing with an `AbortError` when none is given. They are read once for all that asks.
 * Closing it is for good.
 */
export function openableReader({ file, source, readWhole }: OpenableFile): ClosableReader {
  return openingReader(async (signal) => {
    if (!isBlobBacked(file)) return sourceReader(file.id, source);
    const whole = await readWhole(signal);
    if (!whole) throw noPasswordGiven();
    return blobReader(whole);
  });
}
