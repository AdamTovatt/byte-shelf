import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ContentReader } from './contentReader';
import { openableReader } from './openableReader';
import type { ContentSource } from '../../services/contentService';
import type { OpenableFile } from '../../services/fileReading';
import type { ShelfFile } from '../../services/shelfService';

const source = {
  navigationUrl: async (id: string) => `/open/${id}`,
  read: async (id: string) => new Blob([`whole of ${id}`]),
  readRange: async (id: string, start: number, end: number) => new Blob([`${id} ${start}-${end}`]),
} as Partial<ContentSource> as ContentSource;

describe('reading a file nothing shows', () => {
  let readWhole: ReturnType<typeof vi.fn<OpenableFile['readWhole']>>;

  function reader(encrypted: boolean) {
    return openableReader({ file: { id: 'f', encrypted } as ShelfFile, source, readWhole });
  }

  /** Has the contents stay on their way, as at a password prompt, until what is answered brings them. */
  function keepWaiting(): (whole: Blob | null) => void {
    let bring: (whole: Blob | null) => void = () => undefined;
    readWhole.mockReturnValueOnce(new Promise((resolve) => { bring = resolve; }));
    return (whole) => bring(whole);
  }

  beforeEach(() => {
    vi.stubGlobal('window', { isSecureContext: true });
    readWhole = vi.fn<OpenableFile['readWhole']>(async () => new Blob(['plain']));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('reads a plain one by its address, asking for nothing', async () => {
    const content = reader(false);

    expect(await content.url()).toBe('/open/f');
    expect(await (await content.blob()).text()).toBe('whole of f');
    expect(await (await content.range(2, 5)).text()).toBe('f 2-5');
    expect(readWhole).not.toHaveBeenCalled();
  });

  it('unlocks an encrypted one once something reads it, once for all that does', async () => {
    const content = reader(true);
    expect(readWhole).not.toHaveBeenCalled();

    const [whole, part] = await Promise.all([content.blob(), content.range(1, 3)]);

    expect([await whole.text(), await part.text()]).toEqual(['plain', 'la']);
    expect(readWhole).toHaveBeenCalledOnce();
  });

  it('fails as given up when no password is given, and asks again when it is read again', async () => {
    readWhole.mockResolvedValueOnce(null);
    const content = reader(true);

    await expect(content.blob()).rejects.toMatchObject({ name: 'AbortError' });

    expect(await (await content.blob()).text()).toBe('plain');
  });

  it('reads a plain one as an encrypted one where its address is not opened', async () => {
    vi.stubGlobal('window', { isSecureContext: false });

    expect(await (await reader(false).blob()).text()).toBe('plain');
    expect(readWhole).toHaveBeenCalledOnce();
  });

  it.each<[string, (content: ContentReader, signal: AbortSignal) => Promise<unknown>]>([
    ['url', (content, signal) => content.url(signal)],
    ['blob', (content, signal) => content.blob(signal)],
    ['range', (content, signal) => content.range(0, 1, signal)],
  ])('ends a %s read as its signal aborts, alone', async (_, read) => {
    const bring = keepWaiting();
    const content = reader(true);
    const aborting = new AbortController();
    const read1 = read(content, aborting.signal);
    const read2 = content.blob();

    aborting.abort();
    await expect(read1).rejects.toMatchObject({ name: 'AbortError' });
    bring(new Blob(['plain']));

    expect(await (await read2).text()).toBe('plain');
    expect(readWhole).toHaveBeenCalledOnce();
  });

  it.each<[string, (content: ContentReader, signal: AbortSignal) => Promise<unknown>]>([
    ['url', (content, signal) => content.url(signal)],
    ['blob', (content, signal) => content.blob(signal)],
    ['range', (content, signal) => content.range(0, 1, signal)],
  ])('does not start a %s read whose signal has aborted', async (_, read) => {
    await expect(read(reader(true), AbortSignal.abort())).rejects.toMatchObject({ name: 'AbortError' });

    expect(readWhole).not.toHaveBeenCalled();
  });

  it('is closed for good: what is being read ends, and nothing more is read', async () => {
    keepWaiting();
    const content = reader(true);
    const reading = content.blob();

    content.close();

    await expect(reading).rejects.toMatchObject({ name: 'AbortError' });
    expect(readWhole.mock.calls[0][0]!.aborted).toBe(true);
    await expect(content.blob()).rejects.toMatchObject({ name: 'AbortError' });
    expect(readWhole).toHaveBeenCalledOnce();
  });

  it('gives what it had read to no one after it is closed', async () => {
    const content = reader(true);
    await content.blob();

    content.close();

    await expect(content.blob()).rejects.toMatchObject({ name: 'AbortError' });
  });

  it('releases its URL as it is closed', async () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:made');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const content = reader(true);
    expect(await content.url()).toBe('blob:made');

    content.close();

    await vi.waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:made'));
  });
});
