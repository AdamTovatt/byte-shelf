import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FileContents } from './fileContents';
import type { ReadFile } from '../../services/contentService';
import type { ShelfFile } from '../../services/shelfService';

/** A read under way: its signal, and how it ends. */
interface Read {
  signal: AbortSignal;
  resolve: (contents: Blob | null) => void;
  reject: (error: Error) => void;
}

/** An unlock under way: where the file was, its signal, and how it ends. */
interface Unlock {
  path: string;
  signal: AbortSignal;
  resolve: (plain: ReadableStream<Uint8Array> | null) => void;
  reject: (error: Error) => void;
}

const LIMIT = 100 * 1024 * 1024;

function blob(text: string): Blob {
  return new Blob([text]);
}

/** What an unlock answers of a file: its contents, streamed. */
function stream(text: string): ReadableStream<Uint8Array> {
  return blob(text).stream();
}

describe('a file\'s contents', () => {
  let reads: Read[];
  let unlocks: Unlock[];
  let contents: FileContents;
  let states: string[];

  function file(sizeBytes = 1, path = '/a'): ReadFile {
    return {
      file: { id: 'f', path, sizeBytes, contentType: 'text/plain' } as ShelfFile,
      reading: {
        source: {} as ReadFile['reading']['source'],
        read: (_file, signal) => new Promise((resolve, reject) => reads.push({ signal: signal!, resolve, reject })),
        unlock: (each, signal) => new Promise((resolve, reject) => unlocks.push({ path: each.path, signal: signal!, resolve, reject })),
      },
    };
  }

  function make(sizeBytes?: number): void {
    contents = new FileContents(file(sizeBytes));
    contents.subscribe(() => states.push(contents.state().status));
  }

  async function text(): Promise<string | undefined> {
    const state = contents.state();
    return state.status === 'read' ? state.contents.text() : undefined;
  }

  /** Why the file is locked; null when nothing failed. */
  function lockedBy(): unknown {
    const state = contents.state();
    return state.status === 'locked' ? state.error : undefined;
  }

  /** Everything answered so far has been seen. */
  async function settled(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
  }

  function becomes(status: string): Promise<void> {
    return vi.waitFor(() => expect(contents.state().status).toBe(status));
  }

  beforeEach(() => {
    reads = [];
    unlocks = [];
    states = [];
    make();
  });

  it('are read as the file opens', async () => {
    contents.open(false);
    reads[0].resolve(blob('a'));

    await becomes('read');
    expect(await text()).toBe('a');
  });

  it('of a file of 100 MiB are read as it opens', () => {
    make(LIMIT);

    contents.open(false);

    expect(reads).toHaveLength(1);
  });

  it('of a larger file wait to be loaded', async () => {
    make(LIMIT + 1);
    contents.open(false);
    expect([contents.state().status, reads.length]).toEqual(['waiting', 0]);

    contents.load();
    reads[0].resolve(blob('a'));

    await becomes('read');
  });

  it('are read again once reading them failed and they are loaded', async () => {
    contents.open(false);
    reads[0].reject(new Error('unreachable'));
    await becomes('failed');

    contents.load();
    expect(contents.state().status).toBe('reading');
    reads[1].resolve(blob('a'));

    await becomes('read');
  });

  it('are read once for the preview and a save', async () => {
    contents.open(false);
    const saved = contents.contents();
    reads[0].resolve(blob('a'));

    expect(await (await saved)!.text()).toBe('a');
    expect(await (await contents.contents())!.text()).toBe('a');
    expect(await text()).toBe('a');
    expect(reads).toHaveLength(1);
  });

  it('are shown once a save has read or unlocked them', async () => {
    make(LIMIT + 1);
    contents.open(false);
    const saved = contents.contents();
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    expect(contents.state().status).toBe('unlocking');
    unlocks[0].resolve(stream('secret'));

    expect(await (await saved)!.text()).toBe('secret');
    expect(await text()).toBe('secret');
    expect(reads).toHaveLength(1);
  });

  it('are unlocked once, by one prompt, whoever asks', async () => {
    contents.open(true);
    const saved = contents.contents();
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    contents.unlock();
    const savedLate = contents.contents();
    unlocks[0].resolve(stream('secret'));

    expect(await (await saved)!.text()).toBe('secret');
    expect(await savedLate).toBe(await saved);
    expect([reads.length, unlocks.length]).toEqual([1, 1]);
    expect(states).toEqual(['reading', 'locked', 'unlocking', 'reading', 'read']);
  });

  it('stay locked when no password is given, and are asked for again only when unlocked', async () => {
    contents.open(true);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    unlocks[0].resolve(null);
    await settled();
    expect(contents.state().status).toBe('locked');

    contents.unlock();
    expect(unlocks).toHaveLength(2);
    unlocks[1].resolve(stream('secret'));
    await becomes('read');
  });

  it('are asked for no password as the file opens unless told to', async () => {
    contents.open(false);
    reads[0].resolve(null);
    await becomes('locked');

    expect(unlocks).toEqual([]);
  });

  it('stay locked, with why, when unlocking them fails, which a save waiting for them is told too', async () => {
    const error = new Error('unreachable');
    contents.open(true);
    const saved = contents.contents();
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    unlocks[0].reject(error);

    await expect(saved).rejects.toBe(error);
    expect(lockedBy()).toBe(error);
    contents.unlock();
    expect(contents.state().status).toBe('unlocking');
  });

  it('are downloaded once when a save wants them as they are unlocked', async () => {
    let download!: ReadableStreamDefaultController<Uint8Array>;
    contents.open(true);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    unlocks[0].resolve(new ReadableStream({ start: (controller) => void (download = controller) }));
    await becomes('reading');

    const saved = contents.contents();
    download.enqueue(new TextEncoder().encode('secret'));
    download.close();

    const state = await vi.waitFor(() => {
      const now = contents.state();
      if (now.status !== 'read') throw new Error(now.status);
      return now;
    });
    expect(await saved).toBe(state.contents);
    expect([state.contents.type, await state.contents.text()]).toEqual(['text/plain', 'secret']);
    expect([reads.length, unlocks.length]).toEqual([1, 1]);
  });

  it('are unlocked by where the file is now', async () => {
    contents.open(false);
    reads[0].resolve(null);
    await becomes('locked');

    contents.update(file(1, '/moved/a'));
    contents.unlock();

    expect(unlocks[0].path).toBe('/moved/a');
  });

  it('are not read again to be saved while they are locked, only unlocked', async () => {
    contents.open(false);
    reads[0].resolve(null);
    await becomes('locked');

    const saved = contents.contents();
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    unlocks[0].resolve(stream('secret'));

    expect(await (await saved)!.text()).toBe('secret');
    expect(reads).toHaveLength(1);
  });

  it('are locked again when their download fails once the password was given', async () => {
    contents.open(true);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    unlocks[0].resolve(new ReadableStream({ start: (controller) => controller.error(new Error('unreachable')) }));

    await vi.waitFor(() => expect(lockedBy()).toEqual(new Error('unreachable')));
    expect(states).toEqual(['reading', 'locked', 'unlocking', 'reading', 'locked']);
  });

  it.each<[string, (read: Read) => void]>([
    ['given late', (read) => read.resolve(blob('late'))],
    ['failing late', (read) => read.reject(new Error('aborted'))],
  ])('stop being read as the file closes, and an answer %s is left, and the read since not started again', async (_how, answer) => {
    contents.open(false);
    contents.close();
    expect(reads[0].signal.aborted).toBe(true);
    contents.open(false);

    answer(reads[0]);
    await settled();
    contents.load();

    expect([contents.state().status, reads.length]).toEqual(['reading', 2]);
  });

  it.each<[string, (unlock: Unlock) => void]>([
    ['given late', (unlock) => unlock.resolve(stream('late'))],
    ['declined late', (unlock) => unlock.resolve(null)],
    ['failing late', (unlock) => unlock.reject(new Error('aborted'))],
  ])('stop being asked for as the file closes, and an answer %s is left', async (_how, answer) => {
    contents.open(true);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));

    contents.close();
    expect(unlocks[0].signal.aborted).toBe(true);
    contents.open(false);
    answer(unlocks[0]);
    await settled();

    expect(states).toEqual(['reading', 'locked', 'unlocking', 'reading']);
  });

  it.each<[string, (controller: ReadableStreamDefaultController, signal: AbortSignal) => void]>([
    ['fails', (controller, signal) => controller.error(signal.reason)],
    ['ends', (controller) => controller.close()],
  ])('stop being downloaded as the file closes once the password was given, and a download that %s then is left', async (_how, end) => {
    contents.open(true);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    const { signal } = unlocks[0];
    unlocks[0].resolve(new ReadableStream({ start: (controller) => signal.addEventListener('abort', () => end(controller, signal)) }));
    await vi.waitFor(() => expect(states).toEqual(['reading', 'locked', 'unlocking', 'reading']));

    contents.close();
    contents.open(false);
    await settled();

    expect(states).toEqual(['reading', 'locked', 'unlocking', 'reading', 'reading']);
  });

  it('go on being read, and asked for, for a save once the file closes', async () => {
    contents.open(false);
    const saved = contents.contents();
    contents.close();
    expect(reads[0].signal.aborted).toBe(false);
    reads[0].resolve(null);
    await vi.waitFor(() => expect(unlocks).toHaveLength(1));
    expect(unlocks[0].signal.aborted).toBe(false);
    unlocks[0].resolve(stream('secret'));

    expect(await (await saved)!.text()).toBe('secret');
    expect(contents.state().status).toBe('unread');
  });
});
