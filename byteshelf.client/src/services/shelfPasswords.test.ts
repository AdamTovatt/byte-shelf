// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { filePasswordKey, folderPasswordKey, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import { deletedSource, encryptedFolder, fakeShelf, lastOpened, resetShelf, storedFile } from '../test/encryptedShelf';
import { passwordAsking } from '../test/passwordAsking';
import { shelfDirectory } from '../test/shelfFiles';
import { liveContent, type ContentSource } from './contentService';
import { fileReading, unlockedContents } from './fileReading';
import { shelfPasswords, type ShelfPasswords } from './shelfPasswords';
import type { ShelfFile } from './shelfService';

vi.mock('./contentService', async (original) => ({
  ...(await original<typeof import('./contentService')>()),
  liveContent: (await import('../test/encryptedShelf')).liveContent,
}));

describe('a shelf\'s passwords', () => {
  let prompts: ReturnType<typeof passwordAsking>;
  let passwords: ShelfPasswords;

  /** A file's contents, its password asked for when it is locked. */
  function contents(secret: ShelfFile, source: ContentSource): Promise<Blob | null> {
    return unlockedContents(fileReading(source, passwords.decryption), secret);
  }

  beforeEach(() => {
    sessionStorage.clear();
    resetShelf();
    prompts = passwordAsking();
    passwords = shelfPasswords('docs', prompts.ask);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('asks again for a folder whose kept password no longer matches its check', async () => {
    const vault = await encryptedFolder('/vault', 'new');
    rememberPassword(folderPasswordKey('docs', vault.id), 'old');

    const { answer: asked, prompt } = await prompts.untilAsked(() => passwords.folderPassword({ directory: vault, check: fakeShelf.checks.get(vault.id)! }));
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBeNull();
    await prompt.give('new');

    expect(await asked).toBe('new');
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBe('new');
  });

  it('takes back asking for a folder\'s password as its signal aborts', async () => {
    const vault = await encryptedFolder('/vault', 'right');
    const asking = new AbortController();
    const { answer } = await prompts.untilAsked(() => passwords.folderPassword({ directory: vault, check: fakeShelf.checks.get(vault.id)! }, asking.signal));

    asking.abort();

    expect(await answer).toBeNull();
    expect(prompts.open).toEqual([]);
  });

  it('forgets a kept password that no longer opens a file', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'stale');

    const { answer: asked, prompt } = await prompts.untilAsked(() => contents(secret, liveContent('docs')));

    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
    prompt.cancel();
    await asked;
  });

  it('reads a file only with a kept password that opens it, asking for none and reading nothing while there is none', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const reads: string[] = [];
    const source = deletedSource(null, reads);

    expect(await passwords.decryption.read(secret, source)).toBeNull();
    expect(reads).toEqual([]);

    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'stale');
    expect(await passwords.decryption.read(secret, source)).toBeNull();
    expect(lastOpened.signal.aborted).toBe(true);
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const contents = await passwords.decryption.read(secret, source);
    expect([contents!.type, await contents!.text()]).toEqual(['text/plain', 'contents']);

    expect(prompts.open).toEqual([]);
  });

  it('stops reading a file once its read is aborted', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const reading = new AbortController();
    let opening = false;
    const header = (await fakeShelf.files.get(secret.id)!.content.arrayBuffer()).slice(0, -1);
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

    const read = passwords.decryption.read(secret, source, reading.signal).catch((error: Error) => error.name);
    await vi.waitFor(() => expect(opening).toBe(true));
    reading.abort();

    expect(await read).toBe('AbortError');
  });

  it('unlocks a file with the password asked for, from the one opening of it that checked the password', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const reads: string[] = [];

    const { answer, prompt } = await prompts.untilAsked(() => passwords.decryption.unlock(secret, deletedSource(null, reads)));
    await prompt.give('right');

    expect(await new Response(await answer).text()).toBe('contents');
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBe('right');
    expect(reads).toEqual([secret.id]);
  });

  it('unlocks a file with the password kept, asking for none', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');

    expect(await new Response(await passwords.decryption.unlock(secret, deletedSource(null))).text()).toBe('contents');

    expect(prompts.open).toEqual([]);
  });

  it('unlocks a file where no password can be kept', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    vi.stubGlobal('sessionStorage', {
      getItem: () => null,
      setItem: () => {
        throw new Error('No storage.');
      },
    });

    const { answer, prompt } = await prompts.untilAsked(() => contents(secret, deletedSource(null)));
    await prompt.give('right');

    expect(await (await answer)!.text()).toBe('contents');
    expect(rememberedPassword(filePasswordKey('docs', undefined, secret.id))).toBeNull();
  });

  /** A file's source whose download goes on for ever, as a large one's does, with the password kept; `cancelled` hears it stop. */
  function endlessSource(secret: ShelfFile, cancelled = () => {}): ContentSource {
    rememberPassword(filePasswordKey('docs', undefined, secret.id), 'right');
    const stored = fakeShelf.files.get(secret.id)!.content;
    return {
      ...deletedSource(null),
      open: async (_id, signal) => {
        lastOpened.signal = signal;
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
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const cancelled = vi.fn();

    await (await passwords.decryption.unlock(secret, endlessSource(secret, cancelled)))!.cancel();

    await vi.waitFor(() => expect(cancelled).toHaveBeenCalledTimes(1));
  });

  it('downloads no more of an unlocked file once its unlocking is aborted', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const unlocking = new AbortController();
    await passwords.decryption.unlock(secret, endlessSource(secret), unlocking.signal);
    expect(lastOpened.signal.aborted).toBe(false);

    unlocking.abort();

    expect(lastOpened.signal.aborted).toBe(true);
  });

  it('ends the check of a password still running as the prompt is cancelled', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const source = deletedSource(null);
    let checking = false;
    source.open = (_id, signal) => {
      lastOpened.signal = signal;
      checking = true;
      return new Promise(() => {});
    };

    const { answer, prompt } = await prompts.untilAsked(() => passwords.decryption.unlock(secret, source));
    void prompt.give('right');
    await vi.waitFor(() => expect(checking).toBe(true));
    prompt.cancel();

    expect(await answer).toBeNull();
    expect(lastOpened.signal.aborted).toBe(true);
  });

  it('takes back a prompt whose unlocking is aborted, and opens none for one aborted before', async () => {
    const secret = await storedFile('/secret.txt', 'contents', 'right');
    const asking = new AbortController();
    const { answer } = await prompts.untilAsked(() => passwords.decryption.unlock(secret, deletedSource(null), asking.signal));

    asking.abort();

    expect(await answer).toBeNull();
    expect(prompts.open).toEqual([]);
    expect(await passwords.decryption.unlock(secret, deletedSource(null), asking.signal)).toBeNull();
    expect(prompts.open).toEqual([]);
  });

  it('opens a file read from a deletion with the kept password of the folder it was stored in, asking for none', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    const secret = await storedFile('/vault/secret.txt', 'trashed contents', 'pw');
    rememberPassword(folderPasswordKey('docs', vault.id), 'pw');
    const reads: string[] = [];

    const read = await contents(secret, deletedSource({ directory: vault, check: fakeShelf.checks.get(vault.id)! }, reads));

    expect(await read!.text()).toBe('trashed contents');
    expect(reads).toEqual([secret.id]);
    expect(prompts.open).toEqual([]);
  });

  it('asks for the password of a file read from a deletion that took its folder too, keeping it under that folder', async () => {
    const vault = await encryptedFolder('/vault', 'pw');
    const secret = await storedFile('/vault/secret.txt', 'trashed contents', 'pw');

    const { answer: asked, prompt } = await prompts.untilAsked(() => contents(secret, deletedSource({ directory: vault, check: fakeShelf.checks.get(vault.id)! })));
    await prompt.give('pw');

    expect(await (await asked)!.text()).toBe('trashed contents');
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBe('pw');
  });

  it('answers null when the password prompt is cancelled', async () => {
    const vault = await encryptedFolder('/vault', 'pw');

    const { answer: asked, prompt } = await prompts.untilAsked(() => passwords.folderPassword({ directory: vault, check: fakeShelf.checks.get(vault.id)! }));
    prompt.cancel();

    expect(await asked).toBeNull();
    expect(prompts.open).toEqual([]);
    expect(rememberedPassword(folderPasswordKey('docs', vault.id))).toBeNull();
  });

  describe('of the encrypted files left under a plain folder', () => {
    const reports = shelfDirectory('/reports');
    let left: ShelfFile[];

    beforeEach(async () => {
      left = [await storedFile('/reports/a.txt', 'a', 'one'), await storedFile('/reports/b.txt', 'b', 'two')];
    });

    it.each(['one', 'two'])('is one that opens any of them: %s', async (password) => {
      const { answer, prompt } = await prompts.untilAsked(() => passwords.leftPassword(reports, left));
      await prompt.give(password);

      expect(await answer).toBe(password);
      expect(rememberedPassword(folderPasswordKey('docs', reports.id))).toBe(password);
    });

    it('is none that opens none of them', async () => {
      const { prompt } = await prompts.untilAsked(() => passwords.leftPassword(reports, left));
      await prompt.give('three');

      expect(prompts.open).toEqual([prompt]);
    });

    it('is the one kept under the folder, asking for none', async () => {
      rememberPassword(folderPasswordKey('docs', reports.id), 'two');

      expect(await passwords.leftPassword(reports, left)).toBe('two');
      expect(prompts.open).toEqual([]);
    });
  });

  it('asks for a new one by name, and keeps none', async () => {
    const { answer, prompt } = await prompts.untilAsked(() => passwords.newPassword('reports'));
    await prompt.give('new');

    expect(await answer).toBe('new');
    expect(prompt.request).toEqual({ kind: 'new', name: 'reports' });
    expect(sessionStorage).toHaveLength(0);
  });
});
