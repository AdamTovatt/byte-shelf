// Files as they are read into the page, opened and saved: from a content source, an encrypted one decrypted
// with the passwords this tab keeps.

import { streamBlob } from '../lib/streamBlob';
import { deletedContent, type ContentSource } from './contentService';
import type { DeletionTree, ShelfFile } from './shelfService';

/** Encrypted files of any source, opened with the passwords this tab keeps. */
export interface Decryption {
  /** The file's contents, decrypted with the password kept for it; null when none kept opens it. Asks for none. */
  read: (file: ShelfFile, source: ContentSource, signal?: AbortSignal) => Promise<Blob | null>;
  /**
   * The file's contents, downloaded and decrypted as they are read, with a password asked for when none kept
   * opens it; null when it was not given. Aborting `signal` takes the prompt back and ends the download, as
   * cancelling the stream does.
   */
  unlock: (file: ShelfFile, source: ContentSource, signal?: AbortSignal) => Promise<ReadableStream<Uint8Array> | null>;
}

/** A source's files, and a file's whole contents read from it into the page. */
export interface FileReading {
  source: ContentSource;
  /** The whole contents, decrypted when encrypted; null when it is locked. Asks for no password. */
  read: (file: ShelfFile, signal?: AbortSignal) => Promise<Blob | null>;
  /** A locked file's contents, its password asked for, as `Decryption.unlock` answers them. */
  unlock: (file: ShelfFile, signal?: AbortSignal) => Promise<ReadableStream<Uint8Array> | null>;
}

/** A file and what reads it. */
export interface ReadableFile {
  file: ShelfFile;
  reading: FileReading;
}

/** Reads files from `source`, an encrypted one with `decryption`. */
export function fileReading(source: ContentSource, decryption: Decryption): FileReading {
  return {
    source,
    read: (file, signal) => (file.encrypted ? decryption.read(file, source, signal) : source.read(file.id, signal)),
    unlock: (file, signal) => decryption.unlock(file, source, signal),
  };
}

/**
 * The one file a file deletion took, read from it; null for a deletion of a folder, which is none of the
 * files it took.
 */
export function deletedFile(shelf: string, { deletion, tree, encryptedBy }: DeletionTree, decryption: Decryption): ReadableFile | null {
  const file = tree.files.find((taken) => taken.id === deletion.entryId);
  return file ? { file, reading: fileReading(deletedContent(shelf, deletion.deletionId, encryptedBy ?? null), decryption) } : null;
}

/** A file's whole contents, its password asked for when it is locked; null when it was not given. */
export async function unlockedContents({ read, unlock }: FileReading, file: ShelfFile): Promise<Blob | null> {
  const contents = await read(file);
  if (contents) return contents;
  const unlocked = await unlock(file);
  return unlocked && streamBlob(unlocked, file.contentType);
}

/** One file as it is opened in a tab and saved. */
export interface OpenableFile {
  file: ShelfFile;
  source: ContentSource;
  /** Its whole contents, its password asked for when it is locked; null when it was not given. */
  readWhole: () => Promise<Blob | null>;
}

/** A file opened and saved from what reads it, its whole contents answered by `readWhole` when given. */
export function openable({ file, reading }: ReadableFile, readWhole = () => unlockedContents(reading, file)): OpenableFile {
  return { file, source: reading.source, readWhole };
}
