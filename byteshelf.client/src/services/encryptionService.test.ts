// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from './authService';
import { filesUnder } from './encryptionService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

const directory = { id: 'd', shelfId: 's', parentId: null, path: '/vault', name: 'vault', createdAt: '2026-11-02T00:00:00Z', encrypted: true };

function file(name: string) {
  return { id: name, path: `/vault/${name}`, name };
}

describe('files under a folder', () => {
  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('reads every page of the tree, following each page\'s cursor', async () => {
    const sent = stubServer(async (request) =>
      new URL(request.url).searchParams.get('after') === 'next'
        ? json({ directory, directories: [], files: [file('b')], nextCursor: null })
        : json({ directory, directories: [], files: [file('a')], nextCursor: 'next' }));

    const files = await filesUnder('docs', '/vault');

    expect(files.map((each) => each.name)).toEqual(['a', 'b']);
    expect(sent.map((line) => line.split(' Bearer')[0])).toEqual([
      'GET /api/shelves/docs/tree?under=%2Fvault&limit=1000',
      'GET /api/shelves/docs/tree?under=%2Fvault&limit=1000&after=next',
    ]);
  });
});
