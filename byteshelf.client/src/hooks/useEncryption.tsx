import { useMemo } from 'react';
import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';
import { NameAndReason, NameLine } from '../components/MiddleTruncatedName';
import { runOrToastNamed } from '../components/nameToasts';
import { DEFAULT_WORK_FACTOR, createCheck, decryptBlob, encryptBlob } from '../lib/encryption/age';
import { filePasswordKey, folderPasswordKey, rememberPassword } from '../lib/encryption/passwords';
import {
  encryptDirectory,
  fetchEncryptedFolder,
  filesUnder,
  removeDirectoryEncryption,
  replaceContent,
  treeUnder,
  type EncryptedFolder,
} from '../services/encryptionService';
import { unlockedContents, type FileReading } from '../services/fileReading';
import type { ShelfPasswords } from '../services/shelfPasswords';
import type { Entry, ShelfDirectory, ShelfFile } from '../services/shelfService';
import { useInvalidateShelf } from './useShelfQueries';

/** What a row's menu offers for encryption. */
export interface EncryptionActions {
  encrypt: boolean;
  removeEncryption: boolean;
}

/**
 * Which of Encrypt and Remove encryption an entry's menu offers, `inside` saying whether the entry is
 * under an encrypted folder, or `unknown` (a search result). A file is encrypted on its own, or with
 * the folder's password inside one, where only the folder's encryption is removed. A folder inside an
 * encrypted one has neither: it is encrypted with it. A folder offers each again while its counts show
 * the last run left files to finish: plain files under an encrypted folder, encrypted ones under a
 * plain folder.
 */
export function encryptionActions(entry: Entry, inside: 'encrypted' | 'plain' | 'unknown'): EncryptionActions {
  if (entry.kind === 'file') return { encrypt: !entry.value.encrypted, removeEncryption: entry.value.encrypted && inside === 'plain' };
  if (inside !== 'plain') return { encrypt: false, removeEncryption: false };
  return entry.value.encrypted
    ? { encrypt: entry.encryptedFileCount < entry.fileCount, removeEncryption: true }
    : { encrypt: true, removeEncryption: entry.encryptedFileCount > 0 };
}

/** Encrypting and decrypting what is on a shelf, in place. */
export interface Encryption {
  /**
   * Encrypts a file, or a folder and every file under it, in place. An encrypted folder whose last
   * run left plain files has them encrypted with its password.
   */
  encrypt: (entry: Entry) => Promise<void>;
  /**
   * Decrypts a file encrypted on its own, or stops encrypting a folder and decrypts every file under
   * it. A plain folder whose last run left encrypted files has them decrypted with the password
   * that opens them.
   */
  removeEncryption: (entry: Entry) => Promise<void>;
}

/**
 * Encrypting and decrypting a file or a folder of one shelf in place, its live files read by `reading`, saying
 * how far it has got.
 * `workFactor` is what new files and folders are encrypted at; only tests lower it.
 */
export function useEncryption(shelf: string, passwords: ShelfPasswords, reading: FileReading, workFactor = DEFAULT_WORK_FACTOR): Encryption {
  const invalidate = useInvalidateShelf();

  return useMemo(() => {
    const { folderPassword, leftPassword, newPassword, opens } = passwords;

    async function encryptInPlace(file: ShelfFile, password: string): Promise<ShelfFile> {
      return replaceContent(shelf, file, await encryptBlob(await reading.source.read(file.id), password, file.contentType, workFactor), true);
    }

    async function decryptInPlace(file: ShelfFile, password: string): Promise<ShelfFile> {
      return replaceContent(shelf, file, await decryptBlob(await reading.source.read(file.id), password, file.contentType), false);
    }

    async function encrypt(entry: Entry): Promise<void> {
      const { name } = entry.value;
      await runOrToastNamed(async () => {
        if (entry.kind === 'file') {
          const file = entry.value;
          const folder = await fetchEncryptedFolder(shelf, file.path);
          const password = folder ? await folderPassword(folder) : await newPassword(name);
          if (password === null) return;
          await inPlace('Encrypting', name, 'Encrypt', [file], async (each) => {
            const stored = await encryptInPlace(each, password);
            if (!folder) rememberPassword(filePasswordKey(shelf, undefined, stored.id), password);
          });
          return;
        }

        const directory = entry.value;
        const password = directory.encrypted ? await folderPassword(await ownFolder(directory)) : await encryptFolder(directory);
        if (password === null) return;

        // Listed once the folder is encrypted, when no plain file can arrive any more.
        const plain = (await filesUnder(shelf, directory.path)).filter((file) => !file.encrypted);
        await inPlace('Encrypting', name, 'Encrypt', plain, (file) => encryptInPlace(file, password));
      }, 'Could not encrypt', name);
    }

    /**
     * Marks a plain folder encrypted with a new password, asked for; its files are left to the caller.
     * Null when cancelled.
     */
    async function encryptFolder(directory: ShelfDirectory): Promise<string | null> {
      const password = await newPassword(directory.name);
      if (password === null) return null;

      // Refused before anything changes: once the folder is encrypted, a file under it could no
      // longer be decrypted with a password of its own.
      const others: string[] = [];
      for (const file of (await filesUnder(shelf, directory.path)).filter((each) => each.encrypted)) {
        if (!(await opens(file, password))) others.push(file.name);
      }
      if (others.length > 0) throw new Error(`${others.join(', ')} ${others.length === 1 ? 'is' : 'are'} encrypted with another password. Remove that encryption first.`);

      await encryptDirectory(shelf, directory.id, await createCheck(password, workFactor));
      rememberPassword(folderPasswordKey(shelf, directory.id), password);
      invalidate();
      return password;
    }

    /** An encrypted folder with its check. */
    async function ownFolder(directory: ShelfDirectory): Promise<EncryptedFolder> {
      const folder = await fetchEncryptedFolder(shelf, directory.path);
      if (folder?.directory.id !== directory.id) throw new Error(`${directory.name} is no longer an encrypted folder.`);
      return folder;
    }

    async function removeEncryption(entry: Entry): Promise<void> {
      const { name } = entry.value;
      await runOrToastNamed(async () => {
        if (entry.kind === 'file') {
          const plain = await unlockedContents(reading, entry.value);
          if (plain === null) return;
          await inPlace('Removing encryption from', name, 'Remove encryption', [entry.value], (file) => replaceContent(shelf, file, plain, false));
          return;
        }

        const directory = entry.value;
        const password = directory.encrypted ? await folderPassword(await ownFolder(directory)) : await leftPassword(directory, await encryptedFilesOf(directory));
        if (password === null) return;
        if (directory.encrypted) {
          await removeDirectoryEncryption(shelf, directory.id);
          invalidate();
        }

        await inPlace('Removing encryption from', name, 'Remove encryption', await encryptedFilesOf(directory), (file) => decryptInPlace(file, password));
      }, 'Could not remove encryption from', name);
    }

    /**
     * The encrypted files a folder's Remove encryption decrypts. Those under an encrypted folder below
     * it are left out: the server refuses to decrypt a file inside an encrypted folder, and that folder
     * has a Remove encryption of its own.
     */
    async function encryptedFilesOf(directory: ShelfDirectory): Promise<ShelfFile[]> {
      const { directories, files } = await treeUnder(shelf, directory.path);
      const sealed = directories.filter((each) => each.encrypted && each.id !== directory.id).map((each) => `${each.path}/`);
      return files.filter((file) => file.encrypted && !sealed.some((prefix) => file.path.startsWith(prefix)));
    }

    /**
     * Runs `step` for each file in turn, showing how far `doing` `name` has got; a file that fails is
     * named at the end and the rest go on. Choosing `action` again finishes what failed.
     */
    async function inPlace(doing: string, name: string, action: string, files: ShelfFile[], step: (file: ShelfFile) => Promise<unknown>): Promise<void> {
      const line = (after: string) => <NameLine before={doing} name={name} after={after} />;
      const id = toast.loading(line('…'));
      const failed: { name: string; reason: string }[] = [];
      for (const [index, file] of files.entries()) {
        if (files.length > 1) toast.loading(line(`: ${index + 1} of ${files.length}`), { id });
        try {
          await step(file);
        } catch (caught) {
          failed.push({ name: file.name, reason: getErrorMessage(caught, 'it failed') });
        }
      }
      invalidate();
      if (failed.length === 0) {
        toast.success(line(': done'), { id });
        return;
      }
      toast.error(
        <span className="flex min-w-0 flex-col">
          {line(`: ${failed.length} of ${files.length} failed.`)}
          {failed.map((each, index) => <NameAndReason key={index} name={each.name} reason={each.reason} />)}
          <span>Choose {action} again to finish.</span>
        </span>,
        { id, duration: 12000 },
      );
    }

    return { encrypt, removeEncryption };
  }, [shelf, workFactor, passwords, reading, invalidate]);
}
