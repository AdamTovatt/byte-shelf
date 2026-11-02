// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { encryptionActions, useEncryption } from './useEncryption';
import { createCheck, decryptBlob, encryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword, rememberedPassword, filePasswordKey } from '../lib/encryption/passwords';
import type { Entry, ShelfDirectory, ShelfFile } from '../services/shelfService';
import { fill, press } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// What these tests encrypt with: age's default takes about a second a file.
const FAST = 10;

/** One shelf in memory, standing in for the server behind encryptionService. */
const shelf = vi.hoisted(() => ({
  files: new Map<string, { file: ShelfFile; content: Blob }>(),
  checks: new Map<string, string>(),
  directories: new Map<string, ShelfDirectory>(),
  stored: [] as string[],
  folderMarks: [] as string[],
  headerReads: [] as string[],
  failing: new Set<string>(),
  unreachable: false,
  /** What happens on the server just before a folder is marked encrypted, such as an upload. */
  beforeMark: async () => {},
}));

vi.mock('../services/encryptionService', () => ({
  fetchEncryptedFolder: async (_shelf: string, path: string) => {
    if (shelf.unreachable) throw new Error('The server cannot be reached.');
    for (const directory of shelf.directories.values()) {
      const check = shelf.checks.get(directory.id);
      if (check && (path === directory.path || path.startsWith(`${directory.path}/`))) return { directory, check };
    }
    return null;
  },
  encryptDirectory: async (_shelf: string, id: string, check: string) => {
    await shelf.beforeMark();
    shelf.folderMarks.push(`encrypt ${id}`);
    shelf.checks.set(id, check);
    return shelf.directories.get(id);
  },
  removeDirectoryEncryption: async (_shelf: string, id: string) => {
    shelf.folderMarks.push(`remove ${id}`);
    shelf.checks.delete(id);
    return shelf.directories.get(id);
  },
  readContent: async (_shelf: string, id: string) => shelf.files.get(id)!.content,
  openContent: async (_shelf: string, id: string) => {
    shelf.headerReads.push(id);
    return shelf.files.get(id)!.content.stream();
  },
  replaceContent: async (_shelf: string, file: ShelfFile, content: Blob, encrypted: boolean) => {
    if (shelf.failing.has(file.name)) throw new Error('the server refused it');
    shelf.stored.push(`${file.name} ${encrypted ? 'encrypted' : 'plain'}`);
    const stored = { ...file, encrypted, contentType: content.type };
    shelf.files.set(file.id, { file: stored, content });
    return stored;
  },
  filesUnder: async (_shelf: string, path: string) =>
    Array.from(shelf.files.values()).map((each) => each.file).filter((file) => file.path.startsWith(`${path}/`)),
}));

function directory(path: string, encrypted = false): ShelfDirectory {
  const created = { id: `dir:${path}`, shelfId: 's', parentId: null, path, name: path.split('/').pop()!, createdAt: '2026-11-02T00:00:00Z', encrypted };
  shelf.directories.set(created.id, created);
  return created;
}

async function file(path: string, text: string, password?: string): Promise<ShelfFile> {
  const plain = new Blob([text], { type: 'text/plain' });
  const created: ShelfFile = {
    id: `file:${path}`, shelfId: 's', directoryId: 'd', path, name: path.split('/').pop()!, contentType: 'text/plain',
    sizeBytes: text.length, sha256: '', createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z', encrypted: password !== undefined,
  };
  shelf.files.set(created.id, { file: created, content: password === undefined ? plain : await encryptBlob(plain, password, 'text/plain', FAST) });
  return created;
}

async function encryptedFolder(path: string, password: string): Promise<ShelfDirectory> {
  const created = directory(path, true);
  shelf.checks.set(created.id, await createCheck(password, FAST));
  return created;
}

function folderEntry(value: ShelfDirectory, fileCount = 0, encryptedFileCount = 0): Entry {
  return { kind: 'directory', value, sizeBytes: 0, fileCount, encryptedFileCount };
}

async function textOf(path: string, password?: string): Promise<string> {
  const { content, file: stored } = shelf.files.get(`file:${path}`)!;
  return (password === undefined ? content : await decryptBlob(content, password, '')).text().then((text) => `${stored.encrypted ? 'encrypted' : 'plain'} ${text}`);
}

describe('encryption in the browser', () => {
  let root: Root;
  let encryption: ReturnType<typeof useEncryption>;
  const errors: string[] = [];

  function Probe() {
    encryption = useEncryption('docs', FAST);
    return <>{encryption.dialog}</>;
  }

  /** Starts `operation`, then waits for the password dialog it opens. */
  async function untilAsked<T>(operation: () => Promise<T>): Promise<{ answer: Promise<T> }> {
    const answer = operation();
    await vi.waitFor(() => expect(document.body.querySelector('form')).not.toBeNull());
    return { answer };
  }

  beforeEach(async () => {
    sessionStorage.clear();
    Object.assign(shelf, { stored: [], folderMarks: [], headerReads: [], unreachable: false, beforeMark: async () => {} });
    shelf.files.clear();
    shelf.checks.clear();
    shelf.directories.clear();
    shelf.failing.clear();
    errors.length = 0;
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(String(message));
      return '';
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><Probe /></QueryClientProvider>));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('asks again for a folder whose kept password no longer matches its check', async () => {
    const vault = await encryptedFolder('/vault', 'new');
    rememberPassword(folderPasswordKey('docs', vault.id), 'old');

    const { answer: asked } = await untilAsked(() => encryption.folderPassword({ directory: vault, check: shelf.checks.get(vault.id)! }));
    fill('Password', 'new');
    await press('Unlock');

    expect(await asked).toBe('new');
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBe('new');
  });

  it('forgets a kept password that no longer opens a file, and answers null when the prompt is cancelled', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'stale');

    const { answer: asked } = await untilAsked(() => encryption.contents(secret));
    await press('Cancel');

    expect(await asked).toBeNull();
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
  });

  it('refuses to encrypt a folder holding a file under another password, before marking the folder', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a');
    const other = await file('/reports/b.txt', 'b', 'another');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(errors).toEqual(['b.txt is encrypted with another password. Remove that encryption first.']);
    expect([shelf.folderMarks, shelf.stored]).toEqual([[], []]);
    expect(shelf.headerReads).toEqual([other.id]);
  });

  it('encrypts a folder\'s plain files once it is marked, those uploaded just before included, leaving those already encrypted with its password', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a');
    await file('/reports/deeper/b.txt', 'b', 'pw');
    shelf.beforeMark = async () => {
      await file('/reports/late.txt', 'late');
    };

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 2, 1)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(shelf.folderMarks).toEqual([`encrypt ${reports.id}`]);
    expect(shelf.stored).toEqual(['a.txt encrypted', 'late.txt encrypted']);
    expect(await textOf('/reports/a.txt', 'pw')).toBe('encrypted a');
    expect(rememberedPassword(folderPasswordKey('docs', reports.id))).toBe('pw');
  });

  it('finishes an encrypted folder a stopped run left plain files in, with its own password', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    await file('/vault/left.txt', 'left');
    await file('/vault/done.txt', 'done', 'pw');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(vault, 2, 1)));
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(shelf.folderMarks).toEqual([]);
    expect(shelf.stored).toEqual(['left.txt encrypted']);
  });

  it('removes a folder\'s encryption, then decrypts only its encrypted files', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await file('/vault/a.txt', 'a', 'pw');
    await file('/vault/plain.txt', 'plain');

    await act(() => encryption.removeEncryption(folderEntry(vault, 2, 1)));

    expect(shelf.folderMarks).toEqual([`remove ${vault.id}`]);
    expect(shelf.stored).toEqual(['a.txt plain']);
    expect(await textOf('/vault/a.txt')).toBe('plain a');
  });

  it('finishes a plain folder a stopped run left encrypted files in, with the password that opens them', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a', 'pw');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    fill('Password', 'wrong');
    await press('Unlock');
    expect(document.body.textContent).toContain('That is not the password.');
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(shelf.stored).toEqual(['a.txt plain']);
  });

  it('goes on past a file that fails, then names it and how to finish', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await file('/vault/a.txt', 'a');
    await file('/vault/b.txt', 'b');
    shelf.failing.add('a.txt');

    await act(() => encryption.encrypt(folderEntry(vault, 2, 0)));

    expect(shelf.stored).toEqual(['b.txt encrypted']);
    expect(errors).toEqual(['Encrypting vault: 1 of 2 failed. a.txt: the server refused it. Choose Encrypt again to finish.']);
  });

  it('shows a failure to start encrypting a file, rather than dropping it', async () => {
    const plain = await file('/a.txt', 'a');
    shelf.unreachable = true;

    await act(() => encryption.encrypt({ kind: 'file', value: plain }));

    expect(errors).toEqual(['The server cannot be reached.']);
  });
});

describe('the encryption a row offers', () => {
  const plainFile = { kind: 'file', value: { encrypted: false } } as Entry;
  const encryptedFile = { kind: 'file', value: { encrypted: true } } as Entry;
  const folder = (encrypted: boolean, fileCount: number, encryptedFileCount: number) =>
    ({ kind: 'directory', value: { encrypted }, sizeBytes: 0, fileCount, encryptedFileCount }) as Entry;

  it.each([
    ['a plain file', plainFile, 'plain', { encrypt: true, removeEncryption: false }],
    ['an encrypted file', encryptedFile, 'plain', { encrypt: false, removeEncryption: true }],
    ['a plain file in an encrypted folder', plainFile, 'encrypted', { encrypt: true, removeEncryption: false }],
    ['an encrypted file in an encrypted folder', encryptedFile, 'encrypted', { encrypt: false, removeEncryption: false }],
    ['an encrypted file found by search', encryptedFile, 'unknown', { encrypt: false, removeEncryption: false }],
    ['a plain folder', folder(false, 2, 0), 'plain', { encrypt: true, removeEncryption: false }],
    ['a plain folder with encrypted files left', folder(false, 2, 1), 'plain', { encrypt: true, removeEncryption: true }],
    ['an encrypted folder', folder(true, 2, 2), 'plain', { encrypt: false, removeEncryption: true }],
    ['an encrypted folder with plain files left', folder(true, 2, 1), 'plain', { encrypt: true, removeEncryption: true }],
    ['a folder in an encrypted folder', folder(false, 2, 1), 'encrypted', { encrypt: false, removeEncryption: false }],
  ] as const)('%s', (_name, entry, inside, expected) => {
    expect(encryptionActions(entry, inside)).toEqual(expected);
  });
});
