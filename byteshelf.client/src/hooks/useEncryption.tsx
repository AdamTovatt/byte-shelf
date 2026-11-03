import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import toast from 'react-hot-toast';
import { getErrorMessage } from '../ui';
import { PasswordDialog, type PasswordRequest } from '../components/PasswordDialog';
import { NameAndReason, NameLine } from '../components/MiddleTruncatedName';
import { runOrToastNamed } from '../components/nameToasts';
import { DEFAULT_WORK_FACTOR, checkMatches, createCheck, decryptBlob, decryptBlobWith, encryptBlob, opensWith } from '../lib/encryption/age';
import { filePasswordKey, folderPasswordKey, forgetPassword, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import {
  encryptDirectory,
  fetchEncryptedFolder,
  filesUnder,
  removeDirectoryEncryption,
  replaceContent,
  treeUnder,
  type EncryptedFolder,
} from '../services/encryptionService';
import { liveContent, openContent, readContent, type ContentSource } from '../services/contentService';
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

interface Pending {
  /** Tells one prompt from the next, even for the same name. */
  id: number;
  request: PasswordRequest;
  resolve: (password: string | null) => void;
}

/**
 * Encrypted files on one shelf, in the browser: their passwords asked for once a tab and kept
 * (`passwords.ts`), their contents decrypted from the stored bytes (never through a link), and
 * encrypting or decrypting a file or a folder in place. `dialog` is the password prompt to render.
 * `workFactor` is what new files and folders are encrypted at; only tests lower it.
 *
 * These jobs stay in one hook: they share the one prompt and the tab's kept passwords, which split
 * apart each would have to take as arguments.
 */
export function useEncryption(shelf: string, workFactor = DEFAULT_WORK_FACTOR) {
  const [pending, setPending] = useState<Pending | null>(null);
  const prompts = useRef(0);
  const invalidate = useInvalidateShelf();

  const ask = useCallback(
    (request: PasswordRequest) => new Promise<string | null>((resolve) => setPending({ id: ++prompts.current, request, resolve })),
    [],
  );

  // Closes only its own prompt: a password check still running when its prompt was cancelled ends
  // after another prompt may have opened, and must leave that one open.
  function settle(settled: Pending, password: string | null): void {
    setPending((current) => (current === settled ? null : current));
    settled.resolve(password);
  }

  const dialog: ReactNode = pending && (
    <PasswordDialog
      key={pending.id}
      request={pending.request}
      onDone={(password) => settle(pending, password)}
      onCancel={() => settle(pending, null)}
    />
  );

  const operations = useMemo(() => {
    /**
     * The password kept under `key` while `opens` still accepts it, else one asked for that `opens`
     * accepts, then kept. Null when cancelled.
     */
    async function unlock(key: string, name: string, opens: (password: string) => Promise<boolean>): Promise<string | null> {
      const kept = rememberedPassword(key);
      if (kept !== null) {
        if (await opens(kept)) return kept;
        forgetPassword(key);
      }

      const password = await ask({ kind: 'unlock', name, verify: opens });
      if (password !== null) rememberPassword(key, password);
      return password;
    }

    /** The folder's password, tested against its check. Null when cancelled. */
    function folderPassword(folder: EncryptedFolder): Promise<string | null> {
      return unlock(folderPasswordKey(shelf, folder.directory.id), folder.directory.name, (given) => checkMatches(folder.check, given));
    }

    /**
     * An encrypted file's contents read from `source`, decrypted with its folder's password or its
     * own, asked for when this tab has none that opens it. Null when cancelled.
     */
    async function contents(file: ShelfFile, source: ContentSource): Promise<Blob | null> {
      const [stored, folder] = await Promise.all([source.read(file.id), source.encryptedFolderOf(file)]);
      let plain: Blob | null = null;
      const password = await unlock(filePasswordKey(shelf, folder?.directory.id, file.id), folder?.directory.name ?? file.name, async (given) => {
        plain = await decryptBlobWith(stored, given, file.contentType);
        return plain !== null;
      });
      return password === null ? null : plain;
    }

    /** Whether `password` opens a stored encrypted file, downloading only as far as its header. */
    async function fileOpensWith(file: ShelfFile, password: string): Promise<boolean> {
      const reading = new AbortController();
      try {
        return await opensWith(await openContent(shelf, file.id, reading.signal), password);
      } finally {
        reading.abort();
      }
    }

    async function encryptInPlace(file: ShelfFile, password: string): Promise<ShelfFile> {
      return replaceContent(shelf, file, await encryptBlob(await readContent(shelf, file.id), password, file.contentType, workFactor), true);
    }

    async function decryptInPlace(file: ShelfFile, password: string): Promise<ShelfFile> {
      return replaceContent(shelf, file, await decryptBlob(await readContent(shelf, file.id), password, file.contentType), false);
    }

    /**
     * Encrypts a file, or a folder and every file under it, in place. An encrypted folder whose last
     * run left plain files has them encrypted with its password.
     */
    async function encrypt(entry: Entry): Promise<void> {
      const { name } = entry.value;
      await runOrToastNamed(async () => {
        if (entry.kind === 'file') {
          const file = entry.value;
          const folder = await fetchEncryptedFolder(shelf, file.path);
          const password = folder ? await folderPassword(folder) : await ask({ kind: 'new', name });
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
      const password = await ask({ kind: 'new', name: directory.name });
      if (password === null) return null;

      // Refused before anything changes: once the folder is encrypted, a file under it could no
      // longer be decrypted with a password of its own.
      const others: string[] = [];
      for (const file of (await filesUnder(shelf, directory.path)).filter((each) => each.encrypted)) {
        if (!(await fileOpensWith(file, password))) others.push(file.name);
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

    /**
     * Decrypts a file encrypted on its own, or stops encrypting a folder and decrypts every file under
     * it. A plain folder whose last run left encrypted files has them decrypted with the password
     * that opens them.
     */
    async function removeEncryption(entry: Entry): Promise<void> {
      const { name } = entry.value;
      await runOrToastNamed(async () => {
        if (entry.kind === 'file') {
          const plain = await contents(entry.value, liveContent(shelf));
          if (plain === null) return;
          await inPlace('Removing encryption from', name, 'Remove encryption', [entry.value], (file) => replaceContent(shelf, file, plain, false));
          return;
        }

        const directory = entry.value;
        const password = directory.encrypted ? await folderPassword(await ownFolder(directory)) : await leftEncryptedPassword(directory);
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
     * The password of the encrypted files left under a plain folder: kept under the folder from when
     * its encryption was removed, or asked for, opening at least one of them. A kept one that opens
     * none of those left, as when they were encrypted with different passwords, is asked for again.
     * Null when cancelled.
     */
    async function leftEncryptedPassword(directory: ShelfDirectory): Promise<string | null> {
      const encrypted = await encryptedFilesOf(directory);
      return unlock(folderPasswordKey(shelf, directory.id), directory.name, async (given) => {
        for (const file of encrypted) {
          if (await fileOpensWith(file, given)) return true;
        }
        return false;
      });
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

    return { folderPassword, contents, encrypt, removeEncryption };
  }, [shelf, workFactor, ask, invalidate]);

  return { dialog, ...operations };
}
