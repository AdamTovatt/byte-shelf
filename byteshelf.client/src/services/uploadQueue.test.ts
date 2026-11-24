// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { decryptBlob } from '../lib/encryption/age';
import { filePasswordKey, folderPasswordKey, forgetPasswords, rememberPassword, rememberedPassword } from '../lib/encryption/passwords';
import type { OpenFolder } from '../lib/openFolder';
import { FAST } from '../test/encryptedShelf';
import { inAnHour, json, stubServer } from '../test/fakeServer';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { passwordAsking, type ShownPrompt } from '../test/passwordAsking';
import { uploadShelf } from '../test/uploadShelf';
import { saveTokens } from './authService';
import { uploadQueue, type QueueUploads, type UploadState } from './uploadQueue';

/** How often a password was checked against a folder's check. */
const checks = vi.hoisted(() => ({ count: 0 }));

vi.mock('../lib/encryption/age', async (original) => {
  const age = await original<typeof import('../lib/encryption/age')>();
  return {
    ...age,
    checkMatches: (check: string, password: string) => {
      checks.count++;
      return age.checkMatches(check, password);
    },
  };
});

describe('uploads', () => {
  let shelf: ReturnType<typeof uploadShelf>;
  let requests: string[];
  let queueUploads: QueueUploads;
  let first: ReturnType<typeof passwordAsking>;
  let prompts: ReturnType<typeof passwordAsking>;
  let changed: ReturnType<typeof vi.fn<() => void>>;
  /** What was told of each upload, by its file's name. */
  let states: Map<string, UploadState[]>;
  let ids: Set<number>;

  beforeEach(() => {
    states = new Map();
    ids = new Set();
    checks.count = 0;
    localStorage.clear();
    sessionStorage.clear();
    forgetPasswords();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    shelf = uploadShelf('/a');
    requests = stubServer((request) => shelf.answer(request));
    first = passwordAsking();
    prompts = passwordAsking();
    changed = vi.fn();
    queueUploads = uploadQueue({
      askFirst: first.ask,
      ask: prompts.ask,
      report: ({ id, file }, state) => {
        ids.add(id);
        states.set(file.name, [...(states.get(file.name) ?? []), state]);
      },
      changed,
      workFactor: FAST,
    });
  });

  afterEach(() => void vi.unstubAllGlobals());

  /** The folder as it is read now. */
  function folderNow(): OpenFolder {
    return { directory: shelf.directory, encryptedBy: shelf.encryptedBy };
  }

  function start(names: string[], folder = folderNow()): Promise<void> {
    return queueUploads('docs', folder, names.map((name) => new File(['new'], name, { type: 'text/plain' })));
  }

  function last(name: string): UploadState | undefined {
    return states.get(name)?.at(-1);
  }

  /** Waits until the last that is told of `name` is `status`. */
  function until<S extends UploadState['status']>(name: string, status: S): Promise<Extract<UploadState, { status: S }>> {
    return vi.waitFor(() => {
      expect(last(name)?.status).toBe(status);
      return last(name) as Extract<UploadState, { status: S }>;
    }, { timeout: 5000 });
  }

  /** Waits until a password is asked for what is called `name`, in an upload's turn. */
  function asksFor(name: string): Promise<ShownPrompt> {
    return vi.waitFor(() => {
      const shown = prompts.open[0];
      expect(shown?.request.name).toBe(name);
      return shown;
    });
  }

  /** Uploads a.txt, whose name is taken, and replaces the file there. */
  async function replaceTaken(): Promise<void> {
    await start(['a.txt']);
    await replace();
  }

  async function replace(name = 'a.txt'): Promise<void> {
    const taken = await until(name, 'taken');
    states.delete(name);
    taken.replace();
  }

  /** What the last file sent holds, decrypted with `password`. */
  async function sentText(password: string): Promise<string> {
    return (await decryptBlob(shelf.sent.at(-1)!.contents, password, '')).text();
  }

  function sentReplacing(): boolean {
    return shelf.sent.some(({ overwrite }) => overwrite);
  }

  it('tells what shows the shelf as one is stored', async () => {
    await start(['a.txt']);
    await until('a.txt', 'done');

    expect(changed).toHaveBeenCalledOnce();
  });

  it('offers a plain file whose name is taken for replacing, then sends it plain with overwrite', async () => {
    await shelf.put('a.txt');

    await replaceTaken();
    await until('a.txt', 'done');

    expect(requests.filter((line) => line.startsWith('PUT')).map((line) => line.split(' Bearer')[0])).toEqual([
      'PUT /api/shelves/docs/content?path=%2Fa%2Fa.txt&overwrite=false',
      'PUT /api/shelves/docs/content?path=%2Fa%2Fa.txt&overwrite=true',
    ]);
    expect(await shelf.sent[1].contents.text()).toBe('new');
    expect(states.get('a.txt')).toContainEqual({ status: 'uploading', sentBytes: 3 });
  });

  it('reports any other refusal as failed, with its reason, also where there is a file', async () => {
    await shelf.put('a.txt');
    shelf.fails = ({ method }) => method === 'PUT';

    await start(['a.txt']);

    expect((await until('a.txt', 'failed')).error).toMatchObject({ message: 'It is down.' });
  });

  it('reports a conflict with no file at its path as failed, as with a directory there', async () => {
    shelf.refusal = "'/a/a.txt' is a directory.";

    await start(['a.txt']);

    expect((await until('a.txt', 'failed')).error).toMatchObject({ message: "'/a/a.txt' is a directory." });
  });

  it('reports a conflict as failed where it cannot be known whether there is a file', async () => {
    await shelf.put('a.txt');
    shelf.fails = ({ method, url }) => method === 'GET' && new URL(url).pathname.endsWith('/files');

    await start(['a.txt']);

    expect((await until('a.txt', 'failed')).error).toMatchObject({ message: "A file already exists at '/a/a.txt'." });
  });

  it('offers what is replaced again, with the reason, as that is refused', async () => {
    await shelf.put('a.txt');
    await start(['a.txt']);
    await until('a.txt', 'taken');
    shelf.fails = ({ method }) => method === 'PUT';

    await replace();

    expect((await until('a.txt', 'taken')).failure).toMatchObject({ message: 'It is down.' });
  });

  describe('replacing a file with a password of its own', () => {
    let key: string;

    beforeEach(async () => {
      key = filePasswordKey('docs', undefined, (await shelf.put('a.txt', 'own')).id);
    });

    it('encrypts with the password kept for it, kept for the file it becomes, asking for none', async () => {
      rememberPassword(key, 'own');

      await replaceTaken();
      await until('a.txt', 'done');

      expect(shelf.sent.at(-1)).toMatchObject({ overwrite: true, encrypted: true, type: 'text/plain' });
      expect(await sentText('own')).toBe('new');
      expect(rememberedPassword(filePasswordKey('docs', undefined, shelf.file('a.txt').id))).toBe('own');
      expect(prompts.open).toEqual([]);
    });

    it('encrypts with the password asked for, once it opens the file', async () => {
      await replaceTaken();
      const prompt = await asksFor('a.txt');
      await prompt.give('wrong');
      expect(prompts.open).toEqual([prompt]);
      expect(sentReplacing()).toBe(false);

      await prompt.give('own');
      await until('a.txt', 'done');

      expect(shelf.sent.at(-1)).toMatchObject({ overwrite: true, encrypted: true });
      expect(await sentText('own')).toBe('new');
      expect(rememberedPassword(filePasswordKey('docs', undefined, shelf.file('a.txt').id))).toBe('own');
    });

    it('stores nothing with the prompt cancelled, and offers it again as it did', async () => {
      await replaceTaken();
      (await asksFor('a.txt')).cancel();

      expect((await until('a.txt', 'taken')).failure).toBeUndefined();
      expect(sentReplacing()).toBe(false);

      await replace();
      await (await asksFor('a.txt')).give('own');
      await until('a.txt', 'done');
      expect(await sentText('own')).toBe('new');
    });

    it.each([
      ['what encrypts the folder', '/directories/encryption'],
      ['the file there', '/files'],
      ['the folder', '/tree'],
    ])('stores nothing as looking up %s fails, and offers it again with the reason', async (_, path) => {
      rememberPassword(key, 'own');
      await start(['a.txt']);
      await until('a.txt', 'taken');
      shelf.fails = ({ url }) => new URL(url).pathname.endsWith(path);

      await replace();

      expect((await until('a.txt', 'taken')).failure).toMatchObject({ message: 'It is down.' });
      expect(sentReplacing()).toBe(false);

      shelf.fails = () => false;
      await replace();
      await until('a.txt', 'done');
      expect(await sentText('own')).toBe('new');
    });
  });

  describe('into an encrypted folder', () => {
    let key: string;

    beforeEach(async () => {
      key = folderPasswordKey('docs', (await shelf.encrypt('right')).directory.id);
    });

    it('asks for its password before any is queued, once for all, and checks it once', async () => {
      const { answer, prompt } = await first.untilAsked(() => start(['a.txt', 'b.txt']));
      expect(requests).toEqual([]);
      await prompt.give('right');
      await answer;
      await until('b.txt', 'done');

      expect(shelf.sent.map(({ path, encrypted }) => [path, encrypted])).toEqual([['/a/a.txt', true], ['/a/b.txt', true]]);
      expect(await sentText('right')).toBe('new');
      expect(checks.count).toBe(1);
      expect(prompts.open).toEqual([]);
    });

    it('queues none as its password is not given', async () => {
      const { answer, prompt } = await first.untilAsked(() => start(['a.txt', 'b.txt']));
      prompt.cancel();
      await answer;

      expect(requests).toEqual([]);
      expect(states.size).toBe(0);
    });

    it('asks for none with its password kept', async () => {
      rememberPassword(key, 'right');

      await start(['a.txt']);
      await until('a.txt', 'done');

      expect(first.open).toEqual([]);
      expect(shelf.sent).toMatchObject([{ overwrite: false, encrypted: true, type: 'text/plain' }]);
    });
  });

  describe('into a folder that is encrypted since it was read', () => {
    let read: OpenFolder;
    let key: string;

    beforeEach(async () => {
      read = folderNow();
      key = folderPasswordKey('docs', (await shelf.encrypt('right')).directory.id);
    });

    it('encrypts with its kept password, typed as it was', async () => {
      rememberPassword(key, 'right');

      await start(['a.txt'], read);
      await until('a.txt', 'done');

      expect(shelf.sent).toMatchObject([{ overwrite: false, encrypted: true, type: 'text/plain' }]);
      expect(await sentText('right')).toBe('new');
    });

    it('asks for its password once for all that is queued', async () => {
      await start(['a.txt', 'b.txt'], read);
      await (await asksFor('a')).give('right');
      await until('b.txt', 'done');

      expect(shelf.sent.map(({ path, encrypted }) => [path, encrypted])).toEqual([['/a/a.txt', true], ['/a/b.txt', true]]);
      expect(prompts.open).toEqual([]);
    });

    it('ends what is queued with it as its prompt is cancelled, asking once, and nothing else', async () => {
      await start(['a.txt', 'b.txt'], read);
      await start(['c.txt'], read);
      (await asksFor('a')).cancel();

      await until('b.txt', 'cancelled');
      expect(last('a.txt')?.status).toBe('cancelled');
      expect(shelf.sent).toEqual([]);

      await (await asksFor('a')).give('right');
      await until('c.txt', 'done');
      expect(states.get('b.txt')).toEqual([{ status: 'cancelled' }]);
    });
  });

  it('asks for the password of a folder encrypted since the name was taken', async () => {
    await shelf.put('a.txt');
    await start(['a.txt']);
    await until('a.txt', 'taken');
    await shelf.encrypt('right');

    await replace();
    await (await asksFor('a')).give('right');
    await until('a.txt', 'done');

    expect(await sentText('right')).toBe('new');
  });

  it('ends what replaces files of the same batch as the prompt of one is cancelled, offering each again', async () => {
    await shelf.put('a.txt', 'own');
    await shelf.put('b.txt', 'own');
    await start(['a.txt', 'b.txt']);
    await until('b.txt', 'taken');

    await replace('a.txt');
    await replace('b.txt');
    (await asksFor('a.txt')).cancel();

    await until('b.txt', 'taken');
    expect(states.get('b.txt')).toHaveLength(1);
    expect(prompts.open).toEqual([]);
    await replace('b.txt');
    await asksFor('b.txt');
  });

  describe.each([
    ['moved away', () => shelf.replaceFolder(), 'a is no longer where it was.'],
    ['no longer encrypted', () => { shelf.encryptedBy = null; }, 'a is no longer encrypted as it was, or where it was.'],
  ])('into a folder that is %s since it was read', (_, change, reason) => {
    beforeEach(async () => {
      rememberPassword(folderPasswordKey('docs', (await shelf.encrypt('right')).directory.id), 'right');
    });

    it('sends nothing', async () => {
      const read = folderNow();
      change();

      await start(['a.txt'], read);

      expect((await until('a.txt', 'failed')).error).toMatchObject({ message: reason });
      expect(shelf.sent).toEqual([]);
    });

    it('replaces nothing', async () => {
      await shelf.put('a.txt', 'right');
      await start(['a.txt']);
      await until('a.txt', 'taken');
      change();

      await replace();

      expect((await until('a.txt', 'taken')).failure).toMatchObject({ message: reason });
      expect(sentReplacing()).toBe(false);
    });
  });

  it('gives those started apart ids of their own', async () => {
    await start(['a.txt']);
    await start(['b.txt']);
    await until('b.txt', 'done');

    expect(ids.size).toBe(2);
  });

  it('sends one at a time, also of those started apart', async () => {
    const puts: string[] = [];
    let finishFirst = (): void => undefined;
    const held = new Promise<void>((resolve) => { finishFirst = resolve; });
    vi.unstubAllGlobals();
    stubServer(async (request) => {
      if (request.method !== 'PUT') return shelf.answer(request);
      puts.push(new URL(request.url).searchParams.get('path')!);
      await held;
      return json({});
    });

    await start(['a.txt']);
    await start(['b.txt']);
    await vi.waitFor(() => expect(puts).toEqual(['/a/a.txt']));
    // Long enough for the second one's lookups, had they begun.
    for (let turn = 0; turn < 20; turn++) await new Promise((resolve) => setTimeout(resolve));
    expect(puts).toEqual(['/a/a.txt']);
    expect(states.has('b.txt')).toBe(false);

    finishFirst();
    await until('b.txt', 'done');
    expect(puts).toEqual(['/a/a.txt', '/a/b.txt']);
  });
});
