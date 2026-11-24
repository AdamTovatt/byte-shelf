// The passwords of one shelf's encrypted folders and files: each asked for once a tab and kept
// (`passwords.ts`), and what they open, decrypted from the stored bytes (never through a link).

import { eitherAborts } from '../lib/abort';
import { checkMatches, openWith } from '../lib/encryption/age';
import type { AskPassword } from '../lib/encryption/askPassword';
import { filePasswordKey, folderPasswordKey, forgetPassword, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import { streamBlob } from '../lib/streamBlob';
import { liveContent, type ContentSource } from './contentService';
import type { EncryptedFolder } from './encryptionService';
import type { Decryption } from './fileReading';
import type { ShelfDirectory, ShelfFile } from './shelfService';

/**
 * The passwords of a shelf's encrypted folders and files, and what they open. One asked for is kept, and is
 * null when it is not given, as its signal has it.
 */
export interface ShelfPasswords {
  /** An encrypted folder's: the one kept, or one asked for, that passes its check. */
  folderPassword: (folder: EncryptedFolder, signal?: AbortSignal) => Promise<string | null>;
  /** The one a live file has of its own: the one kept, or one asked for, that opens it. */
  filePassword: (file: ShelfFile, signal?: AbortSignal) => Promise<string | null>;
  /**
   * That of the encrypted `files` left under a plain folder: kept under the folder from when its encryption
   * was removed, or asked for, opening at least one of them. A kept one that opens none of those left, as
   * when they were encrypted with different passwords, is asked for again.
   */
  leftPassword: (directory: ShelfDirectory, files: ShelfFile[]) => Promise<string | null>;
  /** A new one, for what is called `name`, not kept. */
  newPassword: (name: string) => Promise<string | null>;
  /** Whether `password` opens a live encrypted file, downloading only as far as its header. */
  opens: (file: ShelfFile, password: string) => Promise<boolean>;
  /** An encrypted file's contents, with the password kept for its folder or for it, and that password asked for. */
  decryption: Decryption;
}

export function shelfPasswords(shelf: string, ask: AskPassword): ShelfPasswords {
  /**
   * The password kept under `key` while `opens` still accepts it, else one asked for that `opens`
   * accepts, then kept. Null when cancelled, as `signal` cancels it.
   */
  async function passwordFor(key: string, name: string, opens: (password: string) => Promise<boolean>, signal?: AbortSignal): Promise<string | null> {
    const kept = rememberedPassword(key);
    if (kept !== null) {
      if (await opens(kept)) return kept;
      forgetPassword(key);
    }

    const password = await ask({ kind: 'unlock', name, verify: opens }, signal);
    if (password !== null) rememberPassword(key, password);
    return password;
  }

  /** What an encrypted file's password is kept under, and what it is the password of: its folder, or itself. */
  async function passwordOf(file: ShelfFile, source: ContentSource): Promise<{ key: string; name: string }> {
    const folder = await source.encryptedFolderOf(file);
    return { key: filePasswordKey(shelf, folder?.directory.id, file.id), name: folder?.directory.name ?? file.name };
  }

  /**
   * A stored file's contents, decrypted as they are downloaded, until the stream is cancelled or `signal`
   * aborts; null when `password` does not open it, nothing past its header downloaded.
   */
  async function decryptedStream(file: ShelfFile, source: ContentSource, password: string, signal?: AbortSignal): Promise<ReadableStream<Uint8Array> | null> {
    const downloading = new AbortController();
    let plain: ReadableStream<Uint8Array> | null = null;
    try {
      plain = await openWith(await source.open(file.id, eitherAborts(downloading.signal, signal)), password);
      return plain;
    } finally {
      if (plain === null) downloading.abort();
    }
  }

  async function opens(file: ShelfFile, password: string): Promise<boolean> {
    const checking = new AbortController();
    try {
      return (await decryptedStream(file, liveContent(shelf), password, checking.signal)) !== null;
    } finally {
      checking.abort();
    }
  }

  const decryption: Decryption = {
    read: async (file, source, signal) => {
      const { key } = await passwordOf(file, source);
      // Nothing is read of a file with no password kept, and no more than its header with a stale one.
      const kept = rememberedPassword(key);
      if (kept === null) return null;
      const plain = await decryptedStream(file, source, kept, signal);
      if (plain) return streamBlob(plain, file.contentType);
      forgetPassword(key);
      return null;
    },
    unlock: async (file, source, signal) => {
      const { key, name } = await passwordOf(file, source);
      // Aborted once no password was given, with a check that is still running then.
      const asking = new AbortController();
      const until = eitherAborts(asking.signal, signal);
      let opened: Promise<ReadableStream<Uint8Array> | null> = Promise.resolve(null);
      const password = await passwordFor(key, name, async (given) => (await (opened = decryptedStream(file, source, given, until))) !== null, signal);
      if (password !== null) return opened;
      asking.abort();
      return null;
    },
  };

  return {
    folderPassword: ({ directory, check }, signal) => passwordFor(folderPasswordKey(shelf, directory.id), directory.name, (given) => checkMatches(check, given), signal),
    filePassword: (file, signal) => passwordFor(filePasswordKey(shelf, undefined, file.id), file.name, (given) => opens(file, given), signal),
    leftPassword: (directory, files) => passwordFor(folderPasswordKey(shelf, directory.id), directory.name, async (given) => {
      for (const file of files) {
        if (await opens(file, given)) return true;
      }
      return false;
    }),
    newPassword: (name) => ask({ kind: 'new', name }),
    opens,
    decryption,
  };
}
