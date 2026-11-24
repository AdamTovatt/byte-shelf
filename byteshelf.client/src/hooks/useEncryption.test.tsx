// @vitest-environment happy-dom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost } from '../contexts/DialogHost';
import { encryptionActions, useEncryption } from './useEncryption';
import { createCheck, decryptBlob, encryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword, rememberedPassword, filePasswordKey } from '../lib/encryption/passwords';
import { liveContent, type ContentSource } from '../services/contentService';
import { fileReading, unlockedContents } from '../services/fileReading';
import type { EncryptedFolder } from '../services/encryptionService';
import type { StoreFolder } from '../services/fileStoring';
import type { Entry, ShelfDirectory, ShelfFile } from '../services/shelfService';
import { fill, press, textOf as messageText } from '../test/dom';

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
}));

vi.mock('../services/encryptionService', () => {
  const filesUnder = (path: string) => Array.from(shelf.files.values()).map((each) => each.file).filter((file) => file.path.startsWith(`${path}/`));
  return {
    fetchEncryptedFolder: async (_shelf: string, path: string, signal?: AbortSignal) => {
      signal?.throwIfAborted();
      return shelf.folderFor(path);
    },
    encryptDirectory: async (_shelf: string, id: string, check: string) => {
      await shelf.beforeMark();
      shelf.folderMarks.push(`encrypt ${id}`);
      shelf.events.push(`encrypt ${id}`);
      shelf.checks.set(id, check);
      return shelf.directories.get(id);
    },
    removeDirectoryEncryption: async (_shelf: string, id: string) => {
      shelf.folderMarks.push(`remove ${id}`);
      shelf.events.push(`remove ${id}`);
      shelf.checks.delete(id);
      return shelf.directories.get(id);
    },
    replaceContent: async (_shelf: string, file: ShelfFile, content: Blob, encrypted: boolean) => {
      if (shelf.failing.has(file.name)) throw new Error('the server refused it');
      shelf.stored.push(`${file.name} ${encrypted ? 'encrypted' : 'plain'}`);
      shelf.events.push(`${file.name} ${encrypted ? 'encrypted' : 'plain'}`);
      const stored = { ...file, encrypted, contentType: content.type };
      shelf.files.set(file.id, { file: stored, content });
      return stored;
    },
    filesUnder: async (_shelf: string, path: string) => filesUnder(path),
    treeUnder: async (_shelf: string, path: string) => ({
      directories: Array.from(shelf.directories.values())
        .filter((directory) => directory.path.startsWith(`${path}/`))
        .map((directory) => ({ ...directory, encrypted: shelf.checks.has(directory.id) })),
      files: filesUnder(path),
    }),
  };
});

vi.mock('../services/contentService', async (original) => ({
  ...(await original<typeof import('../services/contentService')>()),
  liveContent: () => ({
    navigationUrl: async () => '',
    read: async (id: string) => shelf.files.get(id)!.content,
    open: async (id: string, signal: AbortSignal) => {
      shelf.headerReads.push(id);
      signal.addEventListener('abort', () => shelf.headerReadsEnded.push(id));
      await shelf.gate;
      return shelf.files.get(id)!.content.stream();
    },
    encryptedFolderOf: (file: ShelfFile) => shelf.folderFor(file.path),
  }),
}));

/** What was stored, as it was sent; the file stored at a path gets an id of its own. */
const uploads = vi.hoisted(() => [] as { path: string; contents: Blob; type: string; overwrite: boolean; encrypted: boolean }[]);

/** The folder found at a path as it is asked for; another id, or none, once a test has it moved away. */
const folderIds = vi.hoisted(() => new Map<string, string>());

vi.mock('../services/uploadService', () => ({
  uploadFile: async ({ path, file: contents, overwrite, encrypted, signal }: { path: string; file: Blob; overwrite: boolean; encrypted: boolean; signal?: AbortSignal }) => {
    signal?.throwIfAborted();
    uploads.push({ path, contents, type: contents.type, overwrite, encrypted });
    return { id: `stored:${path}`, path, encrypted };
  },
}));

vi.mock('../services/shelfService', async (original) => ({
  ...(await original<typeof import('../services/shelfService')>()),
  fetchFileAt: async (_shelf: string, path: string, signal?: AbortSignal) => {
    signal?.throwIfAborted();
    return Array.from(shelf.files.values()).find(({ file }) => file.path === path)?.file ?? null;
  },
  requireDirectoryAt: async (_shelf: string, { id, name, path }: ShelfDirectory, signal?: AbortSignal) => {
    signal?.throwIfAborted();
    if ((folderIds.get(path) ?? id) !== id) throw new Error(`${name} is no longer where it was.`);
  },
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

/** The signal the last contents of a deletion's file were opened with. */
let opened: AbortSignal;

/** A deletion's files as the fake shelf holds them, stored in `encryptedBy`; each one whose contents are opened is added to `reads`. */
function deletedSource(encryptedBy: EncryptedFolder | null, reads: string[] = []): ContentSource {
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
      opened = signal;
      return shelf.files.get(id)!.content.stream();
    },
    encryptedFolderOf: async () => encryptedBy,
  };
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
  let client: QueryClient;
  let encryption: ReturnType<typeof useEncryption>;
  const errors: string[] = [];

  function Probe() {
    encryption = useEncryption('docs', FAST);
    return null;
  }

  /** A file's contents, its password asked for when it is locked. */
  function contents(secret: ShelfFile, source: ContentSource): Promise<Blob | null> {
    return unlockedContents(fileReading(source, encryption.decryption), secret);
  }

  /** Starts `operation`, then waits for the password dialog it opens. */
  async function untilAsked<T>(operation: () => Promise<T>): Promise<{ answer: Promise<T> }> {
    const answer = operation();
    await vi.waitFor(() => expect(document.body.querySelector('form')).not.toBeNull());
    return { answer };
  }

  beforeEach(async () => {
    sessionStorage.clear();
    Object.assign(shelf, { stored: [], folderMarks: [], headerReads: [], headerReadsEnded: [], gate: null, events: [], unreachable: false, beforeMark: async () => {} });
    shelf.files.clear();
    shelf.checks.clear();
    shelf.directories.clear();
    shelf.failing.clear();
    uploads.length = 0;
    folderIds.clear();
    errors.length = 0;
    vi.spyOn(toast, 'error').mockImplementation((message) => {
      errors.push(messageText(message as ReactNode));
      return '';
    });
    client = new QueryClient();
    vi.spyOn(client, 'invalidateQueries').mockImplementation(async (filters) => {
      if (filters?.queryKey?.[0] === 'shelfContents') shelf.events.push('refetch');
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

  it('asks again for a folder whose kept password no longer matches its check', async () => {
    const vault = await encryptedFolder('/vault', 'new');
    rememberPassword(folderPasswordKey('docs', vault.id), 'old');

    const { answer: asked } = await untilAsked(() => encryption.folderPassword({ directory: vault, check: shelf.checks.get(vault.id)! }));
    fill('Password', 'new');
    await press('Unlock');

    expect(await asked).toBe('new');
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBe('new');
  });

  describe('storing a file', () => {
    const contents = new Blob(['new'], { type: 'image/x-new' });

    /** The folder at `path` as it was read, with the encrypted folder it was in then. */
    function into(path: string, encryptedBy: ShelfDirectory | null = null): StoreFolder {
      const read = shelf.directories.get(`dir:${path}`) ?? directory(path);
      return { directory: read, encryptedBy: encryptedBy && { directory: encryptedBy, check: shelf.checks.get(encryptedBy.id)! } };
    }

    function prompt(): Element | null {
      return document.body.querySelector('form');
    }

    it('stores plain, over what is there, where nothing is encrypted', async () => {
      await encryption.store(into('/a'), '/a/new.txt', contents);

      expect(uploads).toEqual([{ path: '/a/new.txt', contents, type: 'image/x-new', overwrite: true, encrypted: false }]);
    });

    it('stores plain over a plain file, asking for nothing', async () => {
      await file('/a/old.txt', 'old');

      await encryption.store(into('/a'), '/a/old.txt', contents);

      expect(uploads.map(({ encrypted }) => encrypted)).toEqual([false]);
      expect(prompt()).toBeNull();
    });

    describe('in an encrypted folder', () => {
      let vault: ShelfDirectory;

      beforeEach(async () => {
        vault = await encryptedFolder('/vault', 'right');
      });

      it('encrypts it with the folder\'s kept password, typed as it was', async () => {
        rememberPassword(folderPasswordKey('docs', vault.id), 'right');

        const stored = await encryption.store(into('/vault', vault), '/vault/a.txt', contents);

        expect(stored?.id).toBe('stored:/vault/a.txt');
        expect(uploads).toMatchObject([{ path: '/vault/a.txt', type: 'image/x-new', overwrite: true, encrypted: true }]);
        expect(await (await decryptBlob(uploads[0].contents, 'right', '')).text()).toBe('new');
      });

      it('asks for the folder\'s password when none is kept', async () => {
        const { answer } = await untilAsked(() => encryption.store(into('/vault', vault), '/vault/a.txt', contents));
        fill('Password', 'right');
        await press('Unlock');

        expect((await answer)?.id).toBe('stored:/vault/a.txt');
      });

      it('stores nothing with the prompt cancelled', async () => {
        const { answer } = await untilAsked(() => encryption.store(into('/vault', vault), '/vault/a.txt', contents));
        await press('Cancel');

        expect(await answer).toBeNull();
        expect(uploads).toEqual([]);
      });

      it('stores nothing with the prompt taken back', async () => {
        const asking = new AbortController();
        const { answer } = await untilAsked(() => encryption.store(into('/vault', vault), '/vault/a.txt', contents, { asking: asking.signal }));
        act(() => asking.abort());

        expect(await answer).toBeNull();
        expect(uploads).toEqual([]);
        expect(prompt()).toBeNull();
      });

      it('sends nothing once the folder is no longer encrypted as it was read, as one moved away is not', async () => {
        const read = into('/vault', vault);
        shelf.checks.delete(vault.id);

        await expect(encryption.store(read, '/vault/a.txt', contents)).rejects.toThrow('vault is no longer encrypted as it was, or where it was.');
        expect(uploads).toEqual([]);
      });
    });

    it('sends nothing to a folder that has been moved away, though it was plain when it was read', async () => {
      const read = into('/a');
      folderIds.set('/a', 'another');

      await expect(encryption.store(read, '/a/a.txt', contents)).rejects.toThrow('a is no longer where it was.');
      expect(uploads).toEqual([]);
    });

    it('sends nothing to a folder moved away while its password was asked for', async () => {
      const vault = await encryptedFolder('/vault', 'right');
      const { answer } = await untilAsked(() => encryption.store(into('/vault', vault), '/vault/a.txt', contents).catch((error: Error) => error.message));
      folderIds.set('/vault', 'another');
      fill('Password', 'right');
      await press('Unlock');

      expect(await answer).toBe('vault is no longer where it was.');
      expect(uploads).toEqual([]);
    });

    describe('over a file with a password of its own', () => {
      beforeEach(async () => {
        await file('/a/own.txt', 'old', 'own');
      });

      it('encrypts it with that password, kept for the file it becomes', async () => {
        const { answer } = await untilAsked(() => encryption.store(into('/a'), '/a/own.txt', contents));
        fill('Password', 'own');
        await press('Unlock');
        await answer;

        expect(uploads).toMatchObject([{ type: 'image/x-new', overwrite: true, encrypted: true }]);
        expect(await (await decryptBlob(uploads[0].contents, 'own', '')).text()).toBe('new');
        expect(rememberedPassword(filePasswordKey('docs', undefined, 'stored:/a/own.txt'))).toBe('own');
      });

      it('stores nothing with the prompt cancelled', async () => {
        const { answer } = await untilAsked(() => encryption.store(into('/a'), '/a/own.txt', contents));
        await press('Cancel');

        expect(await answer).toBeNull();
        expect(uploads).toEqual([]);
      });

      it('stores nothing with the prompt taken back', async () => {
        const asking = new AbortController();
        const { answer } = await untilAsked(() => encryption.store(into('/a'), '/a/own.txt', contents, { asking: asking.signal }));
        act(() => asking.abort());

        expect(await answer).toBeNull();
        expect(uploads).toEqual([]);
      });

      it('is given up at the prompt as it is ended', async () => {
        const storing = new AbortController();
        const { answer } = await untilAsked(() => encryption.store(into('/a'), '/a/own.txt', contents, { storing: storing.signal }).catch((error: Error) => error.name));
        act(() => storing.abort());

        expect(await answer).toBe('AbortError');
        expect(prompt()).toBeNull();
      });
    });

    it('goes on where a password is no longer asked for, when it needs none', async () => {
      await encryption.store(into('/a'), '/a/a.txt', contents, { asking: AbortSignal.abort() });

      expect(uploads).toHaveLength(1);
    });

    it('looks nothing up once it is ended', async () => {
      const lookedUp = vi.spyOn(shelf, 'folderFor');

      await expect(encryption.store(into('/a'), '/a/a.txt', contents, { storing: AbortSignal.abort() })).rejects.toMatchObject({ name: 'AbortError' });
      expect(lookedUp).not.toHaveBeenCalled();
    });
  });

  it('takes back asking for a folder\'s password as its signal aborts', async () => {
    const vault = await encryptedFolder('/vault', 'right');
    const asking = new AbortController();
    const { answer } = await untilAsked(() => encryption.folderPassword({ directory: vault, check: shelf.checks.get(vault.id)! }, asking.signal));

    act(() => asking.abort());

    expect(await answer).toBeNull();
    expect(document.body.querySelector('form')).toBeNull();
  });

  it('forgets a kept password that no longer opens a file', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'stale');

    const { answer: asked } = await untilAsked(() => contents(secret, liveContent('docs')));

    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
    await press('Cancel');
    await asked;
  });

  it('reads a file only with a kept password that opens it, asking for none and reading nothing while there is none', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const reads: string[] = [];
    const source = deletedSource(null, reads);

    expect(await encryption.decryption.read(secret, source)).toBeNull();
    expect(reads).toEqual([]);

    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'stale');
    expect(await encryption.decryption.read(secret, source)).toBeNull();
    expect(opened.aborted).toBe(true);
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const contents = await encryption.decryption.read(secret, source);
    expect([contents!.type, await contents!.text()]).toEqual(['text/plain', 'contents']);

    await act(async () => {});
    expect(document.body.querySelector('form')).toBeNull();
  });

  it('stops reading a file once its read is aborted', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const reading = new AbortController();
    let opening = false;
    const header = (await shelf.files.get(secret.id)!.content.arrayBuffer()).slice(0, -1);
    const source: ContentSource = {
      ...deletedSource(null),
      // Never past its header, as a slow download, and failing as it is aborted, as one does.
      open: async (_id, signal) => new ReadableStream({
        start: (controller) => {
          opening = true;
          controller.enqueue(new Uint8Array(header));
          signal.addEventListener('abort', () => controller.error(signal.reason));
        },
      }),
    };

    const read = encryption.decryption.read(secret, source, reading.signal).catch((error: Error) => error.name);
    await vi.waitFor(() => expect(opening).toBe(true));
    reading.abort();

    expect(await read).toBe('AbortError');
  });

  it('unlocks a file with the password asked for, from the one opening of it that checked the password', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const reads: string[] = [];

    const { answer } = await untilAsked(() => encryption.decryption.unlock(secret, deletedSource(null, reads)));
    fill('Password', 'right');
    await press('Unlock');

    expect(await new Response(await answer).text()).toBe('contents');
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBe('right');
    expect(reads).toEqual([secret.id]);
  });

  it('unlocks a file with the password kept, asking for none', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');

    expect(await new Response(await encryption.decryption.unlock(secret, deletedSource(null))).text()).toBe('contents');

    expect(document.body.querySelector('form')).toBeNull();
  });

  it('unlocks a file where no password can be kept', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    vi.stubGlobal('sessionStorage', {
      getItem: () => null,
      setItem: () => {
        throw new Error('No storage.');
      },
    });

    const { answer } = await untilAsked(() => contents(secret, deletedSource(null)));
    fill('Password', 'right');
    await press('Unlock');

    expect(await (await answer)!.text()).toBe('contents');
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
  });

  /** A file's source whose download goes on for ever, as a large one's does, with the password kept; `cancelled` hears it stop. */
  function endlessSource(secret: ShelfFile, cancelled = () => {}): ContentSource {
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const stored = shelf.files.get(secret.id)!.content;
    return {
      ...deletedSource(null),
      open: async (_id, signal) => {
        opened = signal;
        const reader = stored.stream().getReader();
        return new ReadableStream({
          pull: async (controller) => {
            const { value } = await reader.read();
            if (value) controller.enqueue(value);
          },
          cancel: cancelled,
        });
      },
    };
  }

  it('downloads no more of an unlocked file once its contents are cancelled', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const cancelled = vi.fn();

    await (await encryption.decryption.unlock(secret, endlessSource(secret, cancelled)))!.cancel();

    await vi.waitFor(() => expect(cancelled).toHaveBeenCalledTimes(1));
  });

  it('downloads no more of an unlocked file once its unlocking is aborted', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const unlocking = new AbortController();
    await encryption.decryption.unlock(secret, endlessSource(secret), unlocking.signal);
    expect(opened.aborted).toBe(false);

    unlocking.abort();

    expect(opened.aborted).toBe(true);
  });

  it('ends the check of a password still running as the prompt is cancelled', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const source = deletedSource(null);
    let checking = false;
    source.open = (_id, signal) => {
      opened = signal;
      checking = true;
      return new Promise(() => {});
    };

    const { answer } = await untilAsked(() => encryption.decryption.unlock(secret, source));
    fill('Password', 'right');
    await press('Unlock');
    await vi.waitFor(() => expect(checking).toBe(true));
    await press('Cancel');

    expect(await answer).toBeNull();
    expect(opened.aborted).toBe(true);
  });

  it('takes back a prompt whose unlocking is aborted, and opens none for one aborted before', async () => {
    const secret = await file('/secret.txt', 'contents', 'right');
    const asking = new AbortController();
    const { answer } = await untilAsked(() => encryption.decryption.unlock(secret, deletedSource(null), asking.signal));

    await act(async () => asking.abort());

    expect(await answer).toBeNull();
    expect(document.body.querySelector('form')).toBeNull();
    await act(async () => expect(await encryption.decryption.unlock(secret, deletedSource(null), asking.signal)).toBeNull());
    expect(document.body.querySelector('form')).toBeNull();
  });

  it('cancels a prompt another takes the place of, which stays open', async () => {
    const [first, second] = [await file('/first.txt', 'one', 'right'), await file('/second.txt', 'two', 'right')];
    const { answer: replaced } = await untilAsked(() => encryption.decryption.unlock(first, deletedSource(null)));

    let answer: Promise<ReadableStream<Uint8Array> | null>;
    await act(async () => void (answer = encryption.decryption.unlock(second, deletedSource(null))));

    expect(await replaced).toBeNull();
    expect(document.body.textContent).toContain('Unlock second.txt');
    fill('Password', 'right');
    await press('Unlock');
    expect(await new Response(await answer!).text()).toBe('two');
    expect(document.body.querySelector('form')).toBeNull();
  });

  it('opens a file read from a deletion with the kept password of the folder it was stored in, asking for none', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    const secret = await file('/vault/secret.txt', 'trashed contents', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    const reads: string[] = [];

    const read = await contents(secret, deletedSource({ directory: vault, check: shelf.checks.get(vault.id)! }, reads));

    expect(await read!.text()).toBe('trashed contents');
    expect(reads).toEqual([secret.id]);
    expect(document.body.querySelector('form')).toBeNull();
  });

  it('asks for the password of a file read from a deletion that took its folder too, keeping it under that folder', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    const secret = await file('/vault/secret.txt', 'trashed contents', 'pw');

    const { answer: asked } = await untilAsked(() => contents(secret, deletedSource({ directory: vault, check: shelf.checks.get(vault.id)! })));
    fill('Password', 'pw');
    await press('Unlock');

    expect(await (await asked)!.text()).toBe('trashed contents');
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBe('pw');
  });

  it('answers null when the password prompt is cancelled', async () => {
    const vault = await encryptedFolder('/vault', 'pw');

    const { answer: asked } = await untilAsked(() => encryption.folderPassword({ directory: vault, check: shelf.checks.get(vault.id)! }));
    await press('Cancel');

    expect(await asked).toBeNull();
    expect(document.body.querySelector('form')).toBeNull();
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBeNull();
  });

  it('leaves a newer prompt open when a check its cancelled prompt started ends after it opened', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a', 'pw');
    let release = () => {};
    shelf.gate = new Promise((resolve) => {
      release = resolve;
    });

    const { answer: removing } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    fill('Password', 'pw');
    await press('Unlock');
    await press('Cancel');
    await removing;

    const vault = await encryptedFolder('/vault', 'v');
    const { answer: asked } = await untilAsked(() => encryption.folderPassword({ directory: vault, check: shelf.checks.get(vault.id)! }));
    release();
    await vi.waitFor(() => expect(shelf.headerReadsEnded).toHaveLength(1));
    await act(async () => {});

    expect(document.body.textContent).toContain('Unlock vault');
    fill('Password', 'v');
    await press('Unlock');
    expect(await asked).toBe('v');
    expect(shelf.stored).toEqual([]);
  });

  it('encrypts a file on its own with a new password, kept for the file', async () => {
    const plain = await file('/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt({ kind: 'file', value: plain }));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(shelf.stored).toEqual(['a.txt encrypted']);
    expect(await textOf('/a.txt', 'pw')).toBe('encrypted a');
    expect(rememberedPassword(filePasswordKey('docs', undefined, plain.id))).toBe('pw');
  });

  it('removes the encryption of a file encrypted on its own, with its password', async () => {
    const secret = await file('/secret.txt', 'contents', 'pw');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption({ kind: 'file', value: secret }));
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(shelf.stored).toEqual(['secret.txt plain']);
    expect(await textOf('/secret.txt')).toBe('plain contents');
  });

  it('changes nothing when the new password for a folder is cancelled', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 1, 0)));
    await press('Cancel');
    await running;

    expect([shelf.folderMarks, shelf.stored, errors]).toEqual([[], [], []]);
  });

  it('says so when a folder shown as encrypted is no longer one', async () => {
    const vault = directory('/vault', true);
    await file('/vault/a.txt', 'a');

    await act(() => encryption.encrypt(folderEntry(vault, 1, 0)));

    expect(errors).toEqual(['Could not encrypt vault:' + 'vault is no longer an encrypted folder.']);
    expect(shelf.stored).toEqual([]);
  });

  it('refetches what is shown once a folder is marked, and again once its files are encrypted', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a');

    const { answer: running } = await untilAsked(() => encryption.encrypt(folderEntry(reports, 1, 0)));
    fill('Password', 'pw');
    fill('Password again', 'pw');
    await press('Next');
    await press('Encrypt');
    await running;

    expect(shelf.events).toEqual([`encrypt ${reports.id}`, 'refetch', 'a.txt encrypted', 'refetch']);
  });

  it('refetches what is shown once a folder is unmarked, and again once its files are decrypted', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await file('/vault/a.txt', 'a', 'pw');

    await act(() => encryption.removeEncryption(folderEntry(vault, 1, 1)));

    expect(shelf.events).toEqual([`remove ${vault.id}`, 'refetch', 'a.txt plain', 'refetch']);
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

    expect(errors).toEqual(['Could not encrypt reports:' + 'b.txt is encrypted with another password. Remove that encryption first.']);
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

  it('leaves the files of an encrypted folder below a plain one to that folder\'s own Remove encryption', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a', 'pw');
    await encryptedFolder('/reports/vault', 'sealed');
    await file('/reports/vault/b.txt', 'b', 'sealed');

    const { answer: running } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 1, 1)));
    // Only the files this run decrypts count: the inner folder's password opens none of them.
    fill('Password', 'sealed');
    await press('Unlock');
    expect(document.body.textContent).toContain('That is not the password.');
    fill('Password', 'pw');
    await press('Unlock');
    await running;

    expect(shelf.stored).toEqual(['a.txt plain']);
    expect(errors).toEqual([]);
  });

  it('asks again for a plain folder\'s files left under another password, rather than reusing the one that opened the others', async () => {
    const reports = directory('/reports');
    await file('/reports/a.txt', 'a', 'first');
    await file('/reports/b.txt', 'b', 'second');

    const { answer: first } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 2, 2)));
    fill('Password', 'first');
    await press('Unlock');
    await first;
    expect(shelf.stored).toEqual(['a.txt plain']);

    const { answer: second } = await untilAsked(() => encryption.removeEncryption(folderEntry(reports, 2, 1)));
    fill('Password', 'second');
    await press('Unlock');
    await second;

    expect(shelf.stored).toEqual(['a.txt plain', 'b.txt plain']);
  });

  it('goes on past a file that fails, then names it and how to finish', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    await file('/vault/a.txt', 'a');
    await file('/vault/b.txt', 'b');
    shelf.failing.add('a.txt');

    await act(() => encryption.encrypt(folderEntry(vault, 2, 0)));

    expect(shelf.stored).toEqual(['b.txt encrypted']);
    // The toast's lines, run together as its text is.
    expect(errors).toEqual(['Encrypting vault: 1 of 2 failed.' + 'a.txt:' + 'the server refused it' + 'Choose Encrypt again to finish.']);
  });

  it('shows a failure to start encrypting a file, rather than dropping it', async () => {
    const plain = await file('/a.txt', 'a');
    shelf.unreachable = true;

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
