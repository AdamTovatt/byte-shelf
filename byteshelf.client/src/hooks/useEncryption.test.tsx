// @vitest-environment happy-dom
import { act, useMemo, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost } from '../contexts/DialogHost';
import { encryptionActions, useEncryption, type Encryption } from './useEncryption';
import { decryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword, rememberedPassword, filePasswordKey } from '../lib/encryption/passwords';
import { liveContent } from '../services/contentService';
import { fileReading } from '../services/fileReading';
import type { ShelfPasswords } from '../services/shelfPasswords';
import type { Entry, ShelfDirectory } from '../services/shelfService';
import { fill, press, textOf as messageText } from '../test/dom';
import { FAST, encryptedFolder, fakeShelf, resetShelf, storedDirectory, storedFile } from '../test/encryptedShelf';
import { useShelfPasswords } from './useShelfPasswords';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../services/encryptionService', async () => (await import('../test/encryptedShelf')).encryptionService);

vi.mock('../services/contentService', async (original) => ({
  ...(await original<typeof import('../services/contentService')>()),
  liveContent: (await import('../test/encryptedShelf')).liveContent,
}));

function folderEntry(value: ShelfDirectory, fileCount = 0, encryptedFileCount = 0): Entry {
  return { kind: 'directory', value, sizeBytes: 0, fileCount, encryptedFileCount };
}

async function textOf(path: string, password?: string): Promise<string> {
  const { content, file: stored } = fakeShelf.files.get(`file:${path}`)!;
  return (password === undefined ? content : await decryptBlob(content, password, '')).text().then((text) => `${stored.encrypted ? 'encrypted' : 'plain'} ${text}`);
}

describe('encryption in the browser', () => {
  let root: Root;
  let client: QueryClient;
  let passwords: ShelfPasswords;
  let encryption: Encryption;
  const errors: string[] = [];

  function Probe() {
    const held = useShelfPasswords('docs');
    const { decryption } = held;
    passwords = held;
    const reading = useMemo(() => fileReading(liveContent('docs'), decryption), [decryption]);
    encryption = useEncryption('docs', held, reading, FAST);
    return null;
  }

  /** Starts `operation`, then waits for the password dialog it opens. */
  async function untilAsked<T>(operation: () => Promise<T>): Promise<{ answer: Promise<T> }> {
    const answer = operation();
    await vi.waitFor(() => expect(document.body.querySelector('form')).not.toBeNull());
    return { answer };
  }

  beforeEach(async () => {
    sessionStorage.clear();
    resetShelf();
    errors.length = 0;
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(messageText(message as ReactNode));
      return '';
    });
    client = new QueryClient();
    vi.spyOn(client, 'invalidateQueries').mockImplementation(async (filters) => {
      if (filters?.queryKey?.[0] === 'shelfContents') fakeShelf.events.push('refetch');
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await act(async () => root.render(<QueryClientProvider client={client}><DialogHost><Probe /></DialogHost></QueryClientProvider>));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('leaves a newer prompt open when a check its cancelled prompt started ends after it opened', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a', 'pw');
    let release = () => {};
    fakeShelf.gate = new Promise((resolve) => {
      release = resolve;
    });

    const { answer: removing } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    fill('Password', 'pw');
    await press('Unlock');
    await press('Cancel');
    await removing;

    const vault = await encryptedFolder('/vault', 'v');
    const { answer: asked } = await untilAsked(() => passwords.folderPassword({ directory: vault, check: fakeShelf.checks.get(vault.id)! }));
    release();
    await vi.waitFor(() => expect(fakeShelf.headerReadsEnded).toHaveLength(1));
    await act(async () => {});

    expect(document.body.textContent).toContain('Unlock vault');
    fill('Password', 'v');
    await press('Unlock');
    expect(await asked).toBe('v');
    expect(fakeShelf.stored).toEqual([]);
  });

  it('encrypts a file on its own with a new password, kept for the file', async () => {
    const plain = await storedFile('/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt({ kind: 'file', value: plain }));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(fakeShelf.stored).toEqual(['a.txt encrypted']);
    expect(await textOf('/a.txt', 'pw')).toBe('encrypted a');
    expect(rememberedPassword(filePasswordKey('docs', undefined, plain.id))).toBe('pw');
  });

  it('removes the encryption of a file encrypted on its own, with its password', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'pw');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption({ kind: 'file', value: secret }));
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(fakeShelf.stored).toEqual(['secret.txt plain']);
    expect(await textOf('/secret.txt')).toBe('plain contents');
  });

  it('changes nothing when the new password for a folder is cancelled', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 1, 0)));
    await press('Cancel');
    await running;

    expect([fakeShelf.folderMarks, fakeShelf.stored, errors]).toEqual([[], [], []]);
  });

  it('says so when a folder shown as encrypted is no longer one', async () => {
    const vault = storedDirectory('/vault', true);
    await storedFile('/vault/a.txt', 'a');

    await act(() => encryption.encrypt(folderEntry(vault, 1, 0)));

    expect(errors).toEqual(['Could not encrypt vault:' + 'vault is no longer an encrypted folder.']);
    expect(fakeShelf.stored).toEqual([]);
  });

  it('refetches what is shown once a folder is marked, and again once its files are encrypted', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 1, 0)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(fakeShelf.events).toEqual([`encrypt ${reports.id}`, 'refetch', 'a.txt encrypted', 'refetch']);
  });

  it('refetches what is shown once a folder is unmarked, and again once its files are decrypted', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await storedFile('/vault/a.txt', 'a', 'pw');

    await act(() => encryption.removeEncryption(folderEntry(vault, 1, 1)));

    expect(fakeShelf.events).toEqual([`remove ${vault.id}`, 'refetch', 'a.txt plain', 'refetch']);
  });

  it('refuses to encrypt a folder holding a file under another password, before marking the folder', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a');
    const other = await storedFile('/reports/b.txt', 'b', 'another');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(errors).toEqual(['Could not encrypt reports:' + 'b.txt is encrypted with another password. Remove that encryption first.']);
    expect([fakeShelf.folderMarks, fakeShelf.stored]).toEqual([[], []]);
    expect(fakeShelf.headerReads).toEqual([other.id]);
  });

  it('encrypts a folder\'s plain files once it is marked, those uploaded just before included, leaving those already encrypted with its password', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a');
    await storedFile('/reports/deeper/b.txt', 'b', 'pw');
    fakeShelf.beforeMark = async () => {
      await storedFile('/reports/late.txt', 'late');
    };

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 2, 1)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(fakeShelf.folderMarks).toEqual([`encrypt ${reports.id}`]);
    expect(fakeShelf.stored).toEqual(['a.txt encrypted', 'late.txt encrypted']);
    expect(await textOf('/reports/a.txt', 'pw')).toBe('encrypted a');
    expect(rememberedPassword(folderPasswordKey('docs', reports.id))).toBe('pw');
  });

  it('finishes an encrypted folder a stopped run left plain files in, with its own password', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    await storedFile('/vault/left.txt', 'left');
    await storedFile('/vault/done.txt', 'done', 'pw');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(vault, 2, 1)));
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(fakeShelf.folderMarks).toEqual([]);
    expect(fakeShelf.stored).toEqual(['left.txt encrypted']);
  });

  it('removes a folder\'s encryption, then decrypts only its encrypted files', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await storedFile('/vault/a.txt', 'a', 'pw');
    await storedFile('/vault/plain.txt', 'plain');

    await act(() => encryption.removeEncryption(folderEntry(vault, 2, 1)));

    expect(fakeShelf.folderMarks).toEqual([`remove ${vault.id}`]);
    expect(fakeShelf.stored).toEqual(['a.txt plain']);
    expect(await textOf('/vault/a.txt')).toBe('plain a');
  });

  it('finishes a plain folder a stopped run left encrypted files in, with the password that opens them', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a', 'pw');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    fill('Password', 'wrong');
    await press('Unlock');
    expect(document.body.textContent).toContain('That is not the password.');
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(fakeShelf.stored).toEqual(['a.txt plain']);
  });

  it('leaves the files of an encrypted folder below a plain one to that folder\'s own Remove encryption', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a', 'pw');
    await encryptedFolder('/reports/vault', 'sealed');
    await storedFile('/reports/vault/b.txt', 'b', 'sealed');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    // Only the files this run decrypts count: the inner folder's password opens none of them.
    fill('Password', 'sealed');
    await press('Unlock');
    expect(document.body.textContent).toContain('That is not the password.');
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(fakeShelf.stored).toEqual(['a.txt plain']);
    expect(errors).toEqual([]);
  });

  it('asks again for a plain folder\'s files left under another password, rather than reusing the one that opened the others', async () => {
    const reports = storedDirectory('/reports');
    await storedFile('/reports/a.txt', 'a', 'first');
    await storedFile('/reports/b.txt', 'b', 'second');

    const { answer: first } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 2, 2)));
    fill('Password', 'first');
    await press('Unlock');
    await first;
    expect(fakeShelf.stored).toEqual(['a.txt plain']);

    const { answer: second } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 2, 1)));
    fill('Password', 'second');
    await press('Unlock');
    await second;

    expect(fakeShelf.stored).toEqual(['a.txt plain', 'b.txt plain']);
  });

  it('goes on past a file that fails, then names it and how to finish', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await storedFile('/vault/a.txt', 'a');
    await storedFile('/vault/b.txt', 'b');
    fakeShelf.failing.add('a.txt');

    await act(() => encryption.encrypt(folderEntry(vault, 2, 0)));

    expect(fakeShelf.stored).toEqual(['b.txt encrypted']);
    // The toast's lines, run together as its text is.
    expect(errors).toEqual(['Encrypting vault: 1 of 2 failed.' + 'a.txt:' + 'the server refused it' + 'Choose Encrypt again to finish.']);
  });

  it('shows a failure to start encrypting a file, rather than dropping it', async () => {
    const plain = await storedFile('/a.txt', 'a');
    fakeShelf.unreachable = true;

    await act(() => encryption.encrypt({ kind: 'file', value: plain }));

    expect(errors).toEqual(['Could not encrypt a.txt:' + 'The server cannot be reached.']);
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
