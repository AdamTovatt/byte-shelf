import { describe, expect, it } from 'vitest';
import { directoryEntry, entrySizeBytes, type ShelfFile } from './shelfService';

const directory = { id: 'd', shelfId: 's', parentId: null, path: '/a', name: 'a', createdAt: '2026-11-02T00:00:00Z' };

describe('entrySizeBytes', () => {
  it('counts a listed folder with everything under it, not as nothing', () => {
    expect(entrySizeBytes(directoryEntry({ directory, sizeBytes: 5000, fileCount: 2 }))).toBe(5000);
  });

  it('counts a file as its own size', () => {
    const file = { ...directory, directoryId: 'd', contentType: 'text/plain', sizeBytes: 12, sha256: '', updatedAt: directory.createdAt } as ShelfFile;
    expect(entrySizeBytes({ kind: 'file', value: file })).toBe(12);
  });
});
