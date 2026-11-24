// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { directoryEntry, entrySizeBytes, fetchFileAt, requireDirectoryAt, type ShelfFile } from './shelfService';
import { saveTokens } from './authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';
import { shelfFile } from '../test/shelfFiles';

const directory = { id: 'd', shelfId: 's', parentId: null, path: '/a', name: 'a', createdAt: '2026-11-02T00:00:00Z', encrypted: false };

describe('entrySizeBytes', () => {
  it('counts a listed folder with everything under it, not as nothing', () => {
    expect(entrySizeBytes(directoryEntry({ directory, sizeBytes: 5000, fileCount: 2, encryptedFileCount: 0 }))).toBe(5000);
  });

  it('counts a file as its own size', () => {
    const file = { ...directory, directoryId: 'd', contentType: 'text/plain', sizeBytes: 12, sha256: '', updatedAt: directory.createdAt } as ShelfFile;
    expect(entrySizeBytes({ kind: 'file', value: file })).toBe(12);
  });
});

describe('what is at a path', () => {
  let answer: () => Response;
  let sent: string[];

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    sent = stubServer(async (request) => {
      request.signal.throwIfAborted();
      return answer();
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is the file there', async () => {
    answer = () => json(shelfFile('a.txt'));

    expect(await fetchFileAt('docs', '/a.txt')).toEqual(shelfFile('a.txt'));
    expect(sent).toEqual(['GET /api/shelves/docs/files?path=%2Fa.txt Bearer t']);
  });

  it('is none where the server finds none', async () => {
    answer = () => json({ message: 'There is no such file.', code: 'not_found' }, 404);

    expect(await fetchFileAt('docs', '/a.txt')).toBeNull();
  });

  // Taken for none, an encrypted file there would be stored over with a plain one.
  it('is not none where the server cannot say', async () => {
    answer = () => json({ message: 'Down.' }, 500);

    await expect(fetchFileAt('docs', '/a.txt')).rejects.toThrow('Down.');
  });

  it('is the folder read there before, or it fails', async () => {
    answer = () => json({ directory, directories: [], files: [], nextCursor: null });

    await requireDirectoryAt('docs', directory);
    await expect(requireDirectoryAt('docs', { ...directory, id: 'other' })).rejects.toThrow('a is no longer where it was.');
    expect(sent[0]).toBe('GET /api/shelves/docs/tree?under=%2Fa&limit=1 Bearer t');
  });

  it('fails for a folder that is gone', async () => {
    answer = () => json({ message: 'There is no directory.', code: 'not_found' }, 404);

    await expect(requireDirectoryAt('docs', directory)).rejects.toThrow('There is no directory.');
  });

  it.each<[string, (signal: AbortSignal) => Promise<unknown>]>([
    ['file', (signal) => fetchFileAt('docs', '/a.txt', signal)],
    ['folder', (signal) => requireDirectoryAt('docs', directory, signal)],
  ])('is asked for no longer as the signal of the ask for a %s aborts', async (_, ask) => {
    await expect(ask(AbortSignal.abort())).rejects.toMatchObject({ name: 'AbortError' });
  });
});
