import { expect, it, vi } from 'vitest';
import type { ContentSource } from './contentService';
import { openable, type FileReading } from './fileReading';
import type { ShelfFile } from './shelfService';

it('reads and unlocks a file\'s contents until what waits for them gives up', async () => {
  const read = vi.fn<FileReading['read']>(async () => null);
  const unlock = vi.fn<FileReading['unlock']>(async () => null);
  const file = { id: 'f' } as ShelfFile;
  const { signal } = new AbortController();

  expect(await openable({ file, reading: { source: {} as ContentSource, read, unlock } }).readWhole(signal)).toBeNull();

  expect([read.mock.calls, unlock.mock.calls]).toEqual([[[file, signal]], [[file, signal]]]);
});
