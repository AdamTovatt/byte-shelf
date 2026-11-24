import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LockedError, lockableReader } from './lockableReader';
import type { ContentSource } from '../../services/contentService';
import type { FileReading } from '../../services/fileReading';
import { shelfFile } from '../../test/shelfFiles';

describe('reading a file that may be locked', () => {
  let read: ReturnType<typeof vi.fn<FileReading['read']>>;
  let unlock: ReturnType<typeof vi.fn<FileReading['unlock']>>;

  function reader(encrypted: boolean) {
    const source = { navigationUrl: async (id: string) => `/open/${id}`, read: async (id: string) => new Blob([`whole of ${id}`]) } as Partial<ContentSource> as ContentSource;
    return lockableReader({ file: shelfFile('f', { encrypted }), reading: { source, read, unlock } });
  }

  function streamOf(text: string): ReadableStream<Uint8Array> {
    return new Blob([text]).stream();
  }

  beforeEach(() => {
    vi.stubGlobal('window', { isSecureContext: true });
    read = vi.fn<FileReading['read']>(async () => null);
    unlock = vi.fn<FileReading['unlock']>(async () => streamOf('plain'));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads a plain one by its address, which is not locked', async () => {
    const content = reader(false);

    expect(await content.url()).toBe('/open/f');
    expect(await content.unlock()).toBe(true);
    expect([read.mock.calls, unlock.mock.calls]).toEqual([[], []]);
  });

  it('reads an encrypted one with the password kept, once for all that asks', async () => {
    read.mockResolvedValue(new Blob(['plain']));
    const content = reader(true);

    const [whole, part] = await Promise.all([content.blob(), content.range(1, 3)]);

    expect([await whole.text(), await part.text()]).toEqual(['plain', 'la']);
    expect(read).toHaveBeenCalledOnce();
    expect(unlock).not.toHaveBeenCalled();
  });

  it('fails to read a locked one, asking for nothing', async () => {
    const failure = await reader(true).blob().catch((error: unknown) => error);

    expect(failure).toBeInstanceOf(LockedError);
    expect(failure).toMatchObject({ name: 'LockedError', message: 'f is locked.' });
    expect(unlock).not.toHaveBeenCalled();
  });

  it('reads a locked one once a password is kept', async () => {
    const content = reader(true);
    await expect(content.blob()).rejects.toBeInstanceOf(LockedError);

    read.mockResolvedValue(new Blob(['plain']));
    expect(await (await content.blob()).text()).toBe('plain');
  });

  it('is read as it was unlocked, though no password was kept', async () => {
    const content = reader(true);

    expect(await content.unlock()).toBe(true);

    expect(await (await content.blob()).text()).toBe('plain');
    expect(await content.unlock()).toBe(true);
    expect(unlock).toHaveBeenCalledOnce();
  });

  it('is unlocked with the password kept, asking for none and reading it once', async () => {
    read.mockResolvedValue(new Blob(['plain']));
    const content = reader(true);

    expect(await content.unlock()).toBe(true);
    expect(await (await content.blob()).text()).toBe('plain');

    expect(read).toHaveBeenCalledOnce();
    expect(unlock).not.toHaveBeenCalled();
  });

  it('fails to unlock what cannot be read, asking for nothing', async () => {
    read.mockRejectedValue(new Error('Down.'));

    await expect(reader(true).unlock()).rejects.toThrow('Down.');
    expect(unlock).not.toHaveBeenCalled();
  });

  it('answers that it is locked to what reads it while its password is asked for', async () => {
    let give: (contents: ReadableStream<Uint8Array> | null) => void = () => undefined;
    unlock.mockReturnValue(new Promise((resolve) => { give = resolve; }));
    const content = reader(true);
    const unlocking = content.unlock();
    await vi.waitFor(() => expect(unlock).toHaveBeenCalled());

    await expect(content.blob()).rejects.toBeInstanceOf(LockedError);
    give(streamOf('plain'));

    expect(await unlocking).toBe(true);
    expect(await (await content.blob()).text()).toBe('plain');
  });

  it('reads a plain one whole where its address is not opened, unlocked as it is', async () => {
    vi.stubGlobal('window', { isSecureContext: false });
    read.mockResolvedValue(new Blob(['plain']));
    const content = reader(false);

    expect(await content.unlock()).toBe(true);
    expect(read).not.toHaveBeenCalled();
    expect(await (await content.blob()).text()).toBe('plain');
    expect(read).toHaveBeenCalledOnce();
  });

  it('stays locked when no password is given', async () => {
    unlock.mockResolvedValue(null);
    const content = reader(true);

    expect(await content.unlock()).toBe(false);

    await expect(content.blob()).rejects.toBeInstanceOf(LockedError);
  });

  it.each(['its signal aborts', 'it is closed'])('asks for the password no longer as %s', async (how) => {
    // As the password prompt does: taken back, it answers that none was given.
    unlock.mockImplementation((_, signal) => new Promise((resolve) => signal!.addEventListener('abort', () => resolve(null))));
    const content = reader(true);
    const aborting = new AbortController();
    const unlocking = content.unlock(aborting.signal);
    await vi.waitFor(() => expect(unlock).toHaveBeenCalled());

    if (how === 'it is closed') content.close();
    else aborting.abort();

    await expect(unlocking).rejects.toMatchObject({ name: 'AbortError' });
  });

  it.each([['an encrypted', true], ['a plain', false]])('is closed for good, what was read of %s one with it', async (_, encrypted) => {
    const content = reader(encrypted);
    await content.unlock();
    await content.blob();

    content.close();

    await expect(content.blob()).rejects.toMatchObject({ name: 'AbortError' });
    await expect(content.unlock()).rejects.toMatchObject({ name: 'AbortError' });
  });
});
