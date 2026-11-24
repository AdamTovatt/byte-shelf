import { isBlobBacked } from '../../services/contentService';
import { unlockWhole, type ReadableFile } from '../../services/fileReading';
import { eitherAborts } from '../abort';
import { blobReader, sourceReader, type ContentReader } from './contentReader';
import { openingReader } from './openingReader';

/** Why an encrypted file could not be read: no password kept opens it. */
export class LockedError extends Error {
  constructor(name: string) {
    super(`${name} is locked.`);
    this.name = 'LockedError';
  }
}

/** A reader whose reads never ask for a password: of a file no password kept opens they fail with a `LockedError`, until it is unlocked. */
export interface LockableReader extends ContentReader {
  /**
   * Asks for the password of a locked file, and reads it; answers whether it is unlocked, as one that was not
   * locked is, for which nothing is asked. The page asks for one password at a time: asking for another, or
   * aborting `signal`, takes this back.
   */
  unlock: (signal?: AbortSignal) => Promise<boolean>;
  /** Ends what it is at and lets go of what it read: after it, every read fails with an `AbortError`. */
  close: () => void;
}

/** Reads a file with the passwords kept; a blob-backed one whole, once for all that reads it. */
export function lockableReader({ file, reading }: ReadableFile): LockableReader {
  const { closed, take, ...reader } = openingReader(async (signal) => {
    if (!isBlobBacked(file)) return sourceReader(file.id, reading.source);
    const whole = await reading.read(file, signal);
    if (!whole) throw new LockedError(file.name);
    return blobReader(whole);
  });

  return {
    ...reader,
    unlock: async (signal) => {
      const until = eitherAborts(closed, signal);
      until.throwIfAborted();
      if (!file.encrypted) return true;
      try {
        await reader.blob(until);
        return true;
      } catch (caught) {
        if (!(caught instanceof LockedError)) throw caught;
      }

      const whole = await unlockWhole(reading, file, until);
      until.throwIfAborted();
      if (whole) take(blobReader(whole));
      return whole !== null;
    },
  };
}
