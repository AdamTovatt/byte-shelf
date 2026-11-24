import { expect, it, vi } from 'vitest';
import { shelfFile } from '../../test/shelfFiles';
import type { ContentReader } from './contentReader';
import { readText } from './readText';

it('reads a text until its signal aborts', async () => {
  const range = vi.fn<ContentReader['range']>(async () => new Blob(['text']));
  const { signal } = new AbortController();

  expect(await readText({ file: shelfFile('a.txt'), content: { range } as Partial<ContentReader> as ContentReader }, signal)).toEqual({ text: 'text', cutAt: null });

  expect(range.mock.calls).toEqual([[0, 1024 * 1024 + 1, signal]]);
});
