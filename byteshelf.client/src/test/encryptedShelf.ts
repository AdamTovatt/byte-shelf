import type { ContentSource } from '../services/contentService';
import type { EncryptedFolder } from '../services/encryptionService';
import type { ShelfDirectory, ShelfFile } from '../services/shelfService';
import { createCheck, encryptBlob } from '../lib/encryption/age';
import { shelfDirectory, shelfFile } from './shelfFiles';

/** What tests encrypt with: age's default takes about a second a file. */
export const FAST = 10;

/** One shelf in memory, standing in for the server behind encryptionService and the live content. */
export const fakeShelf = {
  files: new Map<string, { file: ShelfFile; content: Blob }>(),
  checks: new Map<string, string>(),
  directories: new Map<string, ShelfDirectory>(),
  stored: [] as string[],
  folderMarks: [] as string[],
  headerReads: [] as string[],
  /** The files whose header read has ended, the read aborted once it was checked. */
  headerReadsEnded: [] as string[],
  /** Held until it settles, every header read waits for it: a password check still running. */
  gate: null as Promise<void> | null,
  /** What changed, in order: a folder marked or unmarked, a file swapped, what is shown refetched. */
  events: [] as string[],
  failing: new Set<string>(),
  unreachable: false,
  /** What happens on the server just before a folder is marked encrypted, such as an upload. */
  beforeMark: async () => {},
  /** The encrypted folder a file at `path` is, or would be, under, as the server answers it. */
  async folderFor(path: string) {
    if (this.unreachable) throw new Error('The server cannot be reached.');
    for (const directory of this.directories.values()) {
      const check = this.checks.get(directory.id);
      if (check && (path === directory.path || path.startsWith(`${directory.path}/`))) return { directory, check };
    }
    return null;
  },
};

/** The signal the last contents of a deletion's file were opened with. */
export const lastOpened = { signal: new AbortController().signal };

/** Empties the shelf. */
export function resetShelf(): void {
  Object.assign(fakeShelf, { stored: [], folderMarks: [], headerReads: [], headerReadsEnded: [], gate: null, events: [], unreachable: false, beforeMark: async () => {} });
  fakeShelf.files.clear();
  fakeShelf.checks.clear();
  fakeShelf.directories.clear();
  fakeShelf.failing.clear();
  lastOpened.signal = new AbortController().signal;
}

const filesUnder = (path: string) => Array.from(fakeShelf.files.values()).map((each) => each.file).filter((file) => file.path.startsWith(`${path}/`));

/** In place of encryptionService, on the shelf in memory. */
export const encryptionService = {
  fetchEncryptedFolder: async (_shelf: string, path: string, signal?: AbortSignal) => {
    signal?.throwIfAborted();
    return fakeShelf.folderFor(path);
  },
  encryptDirectory: async (_shelf: string, id: string, check: string) => {
    await fakeShelf.beforeMark();
    fakeShelf.folderMarks.push(`encrypt ${id}`);
    fakeShelf.events.push(`encrypt ${id}`);
    fakeShelf.checks.set(id, check);
    return fakeShelf.directories.get(id);
  },
  removeDirectoryEncryption: async (_shelf: string, id: string) => {
    fakeShelf.folderMarks.push(`remove ${id}`);
    fakeShelf.events.push(`remove ${id}`);
    fakeShelf.checks.delete(id);
    return fakeShelf.directories.get(id);
  },
  replaceContent: async (_shelf: string, file: ShelfFile, content: Blob, encrypted: boolean) => {
    if (fakeShelf.failing.has(file.name)) throw new Error('the server refused it');
    fakeShelf.stored.push(`${file.name} ${encrypted ? 'encrypted' : 'plain'}`);
    fakeShelf.events.push(`${file.name} ${encrypted ? 'encrypted' : 'plain'}`);
    const stored = { ...file, encrypted, contentType: content.type };
    fakeShelf.files.set(file.id, { file: stored, content });
    return stored;
  },
  filesUnder: async (_shelf: string, path: string) => filesUnder(path),
  treeUnder: async (_shelf: string, path: string) => ({
    directories: Array.from(fakeShelf.directories.values())
      .filter((directory) => directory.path.startsWith(`${path}/`))
      .map((directory) => ({ ...directory, encrypted: fakeShelf.checks.has(directory.id) })),
    files: filesUnder(path),
  }),
};

/** In place of contentService's live content, on the shelf in memory. */
export const liveContent = () => ({
  navigationUrl: async () => '',
  read: async (id: string) => fakeShelf.files.get(id)!.content,
  open: async (id: string, signal: AbortSignal) => {
    fakeShelf.headerReads.push(id);
    signal.addEventListener('abort', () => fakeShelf.headerReadsEnded.push(id));
    await fakeShelf.gate;
    return fakeShelf.files.get(id)!.content.stream();
  },
  encryptedFolderOf: (file: ShelfFile) => fakeShelf.folderFor(file.path),
});

export function storedDirectory(path: string, encrypted = false): ShelfDirectory {
  const created = shelfDirectory(path, { encrypted });
  fakeShelf.directories.set(created.id, created);
  return created;
}

export async function storedFile(path: string, text: string, password?: string): Promise<ShelfFile> {
  const plain = new Blob([text], { type: 'text/plain' });
  const created = shelfFile(path.split('/').pop()!, { id: `file:${path}`, path, sizeBytes: text.length, encrypted: password !== undefined });
  fakeShelf.files.set(created.id, { file: created, content: password === undefined ? plain : await encryptBlob(plain, password, 'text/plain', FAST) });
  return created;
}

/** A deletion's files as the fake shelf holds them, stored in `encryptedBy`; each one whose contents are opened is added to `reads`. */
export function deletedSource(encryptedBy: EncryptedFolder | null, reads: string[] = []): ContentSource {
  return {
    navigationUrl: async () => '',
    read: async () => {
      throw new Error('An encrypted file is decrypted as it is downloaded.');
    },
    readRange: async () => {
      throw new Error('An encrypted file is read whole.');
    },
    open: async (id, signal) => {
      reads.push(id);
      lastOpened.signal = signal;
      return fakeShelf.files.get(id)!.content.stream();
    },
    encryptedFolderOf: async () => encryptedBy,
  };
}

export async function encryptedFolder(path: string, password: string): Promise<ShelfDirectory> {
  const created = storedDirectory(path, true);
  fakeShelf.checks.set(created.id, await createCheck(password, FAST));
  return created;
}
