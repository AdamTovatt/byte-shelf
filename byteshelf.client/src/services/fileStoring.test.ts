// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { decryptBlob } from '../lib/encryption/age';
import { filePasswordKey, folderPasswordKey, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import { FAST, encryptedFolder, fakeShelf, resetShelf, storedDirectory, storedFile } from '../test/encryptedShelf';
import { passwordAsking } from '../test/passwordAsking';
import { fileStoring, type StoreFile } from './fileStoring';
import type { OpenFolder } from './openFolder';
import { shelfPasswords } from './shelfPasswords';
import type { ShelfDirectory } from './shelfService';

vi.mock('./encryptionService', async () => {
  const { encryptionService } = await import('../test/encryptedShelf');
  return {
    ...encryptionService,
    fetchEncryptedFolder: async (name: string, path: string, signal?: AbortSignal) => {
      steps.take('fetchEncryptedFolder', signal);
      return encryptionService.fetchEncryptedFolder(name, path, signal);
    },
  };
});

vi.mock('./contentService', async (original) => ({
  ...(await original<typeof import('./contentService')>()),
  liveContent: (await import('../test/encryptedShelf')).liveContent,
}));

/** What is asked of the server as a file is stored, in order, and what happens during one of these. */
const steps = vi.hoisted(() => ({
  called: [] as string[],
  signals: [] as (AbortSignal | undefined)[],
  during: (() => undefined) as (step: string) => void,
  take(step: string, signal?: AbortSignal): void {
    this.called.push(step);
    this.signals.push(signal);
    this.during(step);
    signal?.throwIfAborted();
  },
}));

/** What was stored, as it was sent; the file stored at a path gets an id of its own. */
const uploads = vi.hoisted(() => [] as { path: string; contents: Blob; type: string; overwrite: boolean; encrypted: boolean }[]);

/** The folder found at a path as it is asked for; another id, or none, once a test has it moved away. */
const folderIds = vi.hoisted(() => new Map<string, string>());

vi.mock('./uploadService', () => ({
  uploadFile: async ({ path, file: contents, overwrite, encrypted, signal }: { path: string; file: Blob; overwrite: boolean; encrypted: boolean; signal?: AbortSignal }) => {
    steps.take('uploadFile', signal);
    uploads.push({ path, contents, type: contents.type, overwrite, encrypted });
    return { id: `stored:${path}`, path, encrypted };
  },
}));

vi.mock('./shelfService', async (original) => ({
  ...(await original<typeof import('./shelfService')>()),
  fetchFileAt: async (_shelf: string, path: string, signal?: AbortSignal) => {
    steps.take('fetchFileAt', signal);
    return Array.from(fakeShelf.files.values()).find(({ file }) => file.path === path)?.file ?? null;
  },
  requireDirectoryAt: async (_shelf: string, { id, name, path }: ShelfDirectory, signal?: AbortSignal) => {
    steps.take('requireDirectoryAt', signal);
    if ((folderIds.get(path) ?? id) !== id) throw new Error(`${name} is no longer where it was.`);
  },
}));

describe('storing a file', () => {
  const contents = new Blob(['new'], { type: 'image/x-new' });

  /** The folder at `path` as it was read, with the encrypted folder it was in then. */
  function into(path: string, encryptedBy: ShelfDirectory | null = null): OpenFolder {
    const read = fakeShelf.directories.get(`dir:${path}`) ?? storedDirectory(path);
    return { directory: read, encryptedBy: encryptedBy && { directory: encryptedBy, check: fakeShelf.checks.get(encryptedBy.id)! } };
  }

  let prompts: ReturnType<typeof passwordAsking>;
  let store: StoreFile;

  beforeEach(() => {
    sessionStorage.clear();
    resetShelf();
    uploads.length = 0;
    folderIds.clear();
    steps.called = [];
    steps.signals = [];
    steps.during = () => undefined;
    prompts = passwordAsking();
    store = fileStoring('docs', shelfPasswords('docs', prompts.ask), FAST);
  });

  afterEach(() => void vi.restoreAllMocks());

  it('stores plain, over what is there, where nothing is encrypted', async () => {
    await store(into('/a'), '/a/new.txt', contents);

    expect(uploads).toEqual([{ path: '/a/new.txt', contents, type: 'image/x-new', overwrite: true, encrypted: false }]);
  });

  it('stores plain over a plain file, asking for nothing', async () => {
    await storedFile('/a/old.txt', 'old');

    await store(into('/a'), '/a/old.txt', contents);

    expect(uploads.map(({ encrypted }) => encrypted)).toEqual([false]);
    expect(prompts.open).toEqual([]);
  });

  describe('in an encrypted folder', () => {
    let vault: ShelfDirectory;

    beforeEach(async () => {
      vault = await encryptedFolder('/vault', 'right');
    });

    it('encrypts it with the folder\'s kept password, typed as it was', async () => {
      rememberPassword(folderPasswordKey('docs', vault.id), 'right');

      const stored = await store(into('/vault', vault), '/vault/a.txt', contents);

      expect(stored?.id).toBe('stored:/vault/a.txt');
      expect(uploads).toMatchObject([{ path: '/vault/a.txt', type: 'image/x-new', overwrite: true, encrypted: true }]);
      expect(await (await decryptBlob(uploads[0].contents, 'right', '')).text()).toBe('new');
    });

    it('asks for the folder\'s password when none is kept', async () => {
      const { answer, prompt } = await prompts.untilAsked(() => store(into('/vault', vault), '/vault/a.txt', contents));
      await prompt.give('right');

      expect((await answer)?.id).toBe('stored:/vault/a.txt');
    });

    it('stores nothing with the prompt cancelled', async () => {
      const { answer, prompt } = await prompts.untilAsked(() => store(into('/vault', vault), '/vault/a.txt', contents));
      prompt.cancel();

      expect(await answer).toBeNull();
      expect(uploads).toEqual([]);
    });

    it('stores nothing with the prompt taken back', async () => {
      const asking = new AbortController();
      const { answer } = await prompts.untilAsked(() => store(into('/vault', vault), '/vault/a.txt', contents, { asking: asking.signal }));
      asking.abort();

      expect(await answer).toBeNull();
      expect(uploads).toEqual([]);
      expect(prompts.open).toEqual([]);
    });

    it('sends nothing once the folder is no longer encrypted as it was read, as one moved away is not', async () => {
      const read = into('/vault', vault);
      fakeShelf.checks.delete(vault.id);

      await expect(store(read, '/vault/a.txt', contents)).rejects.toThrow('vault is no longer encrypted as it was, or where it was.');
      expect(uploads).toEqual([]);
    });
  });

  it('sends nothing to a folder that has been moved away, though it was plain when it was read', async () => {
    const read = into('/a');
    folderIds.set('/a', 'another');

    await expect(store(read, '/a/a.txt', contents)).rejects.toThrow('a is no longer where it was.');
    expect(uploads).toEqual([]);
  });

  it('sends nothing to a folder moved away while its password was asked for', async () => {
    const vault = await encryptedFolder('/vault', 'right');
    const { answer, prompt } = await prompts.untilAsked(() => store(into('/vault', vault), '/vault/a.txt', contents).catch((error: Error) => error.message));
    folderIds.set('/vault', 'another');
    await prompt.give('right');

    expect(await answer).toBe('vault is no longer where it was.');
    expect(uploads).toEqual([]);
  });

  describe('over a file with a password of its own', () => {
    beforeEach(async () => {
      await storedFile('/a/own.txt', 'old', 'own');
    });

    it('encrypts it with that password, kept for the file it becomes', async () => {
      const { answer, prompt } = await prompts.untilAsked(() => store(into('/a'), '/a/own.txt', contents));
      await prompt.give('own');
      await answer;

      expect(uploads).toMatchObject([{ type: 'image/x-new', overwrite: true, encrypted: true }]);
      expect(await (await decryptBlob(uploads[0].contents, 'own', '')).text()).toBe('new');
      expect(rememberedPassword(filePasswordKey('docs', undefined, 'stored:/a/own.txt'))).toBe('own');
    });

    it('encrypts it with that password where it is kept, asking for none', async () => {
      rememberPassword(filePasswordKey('docs', undefined, 'file:/a/own.txt'), 'own');

      await store(into('/a'), '/a/own.txt', contents);

      expect(prompts.open).toEqual([]);
      expect(await (await decryptBlob(uploads[0].contents, 'own', '')).text()).toBe('new');
    });

    it('stores nothing with the prompt cancelled', async () => {
      const { answer, prompt } = await prompts.untilAsked(() => store(into('/a'), '/a/own.txt', contents));
      prompt.cancel();

      expect(await answer).toBeNull();
      expect(uploads).toEqual([]);
    });

    it('stores nothing with the prompt taken back', async () => {
      const asking = new AbortController();
      const { answer } = await prompts.untilAsked(() => store(into('/a'), '/a/own.txt', contents, { asking: asking.signal }));
      asking.abort();

      expect(await answer).toBeNull();
      expect(uploads).toEqual([]);
    });

    it('is given up at the prompt as it is ended', async () => {
      const storing = new AbortController();
      const { answer } = await prompts.untilAsked(() => store(into('/a'), '/a/own.txt', contents, { storing: storing.signal }).catch((error: Error) => error.name));
      storing.abort();

      expect(await answer).toBe('AbortError');
      expect(prompts.open).toEqual([]);
    });
  });

  it('goes on where a password is no longer asked for, when it needs none', async () => {
    await store(into('/a'), '/a/a.txt', contents, { asking: AbortSignal.abort() });

    expect(uploads).toHaveLength(1);
  });

  it('looks nothing up once it is ended', async () => {
    const lookedUp = vi.spyOn(fakeShelf, 'folderFor');

    await expect(store(into('/a'), '/a/a.txt', contents, { storing: AbortSignal.abort() })).rejects.toMatchObject({ name: 'AbortError' });
    expect(lookedUp).not.toHaveBeenCalled();
  });

  describe('ended while it is under way', () => {
    let storing: AbortController;

    beforeEach(() => {
      storing = new AbortController();
    });

    it.each([
      ['what encrypts the folder is looked up', 'fetchEncryptedFolder', ['fetchEncryptedFolder']],
      ['the file there is looked up', 'fetchFileAt', ['fetchEncryptedFolder', 'fetchFileAt']],
      ['the folder is looked up', 'requireDirectoryAt', ['fetchEncryptedFolder', 'fetchFileAt', 'requireDirectoryAt']],
      ['it is sent', 'uploadFile', ['fetchEncryptedFolder', 'fetchFileAt', 'requireDirectoryAt', 'uploadFile']],
    ])('goes no further than as %s', async (_, during, called) => {
      steps.during = (step) => {
        if (step === during) storing.abort();
      };

      await expect(store(into('/a'), '/a/a.txt', contents, { storing: storing.signal })).rejects.toMatchObject({ name: 'AbortError' });
      expect(steps.called).toEqual(called);
      expect(steps.signals.every((signal) => signal === storing.signal)).toBe(true);
      expect(uploads).toEqual([]);
    });

    it('sends nothing once it is encrypted', async () => {
      const vault = await encryptedFolder('/vault', 'right');
      rememberPassword(folderPasswordKey('docs', vault.id), 'right');
      const stream = vi.spyOn(contents, 'stream').mockImplementation(() => {
        storing.abort();
        return new Response('new').body!;
      });

      await expect(store(into('/vault', vault), '/vault/a.txt', contents, { storing: storing.signal })).rejects.toMatchObject({ name: 'AbortError' });
      expect(stream).toHaveBeenCalled();
      expect(uploads).toEqual([]);
    });
  });
});
