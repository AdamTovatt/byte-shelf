// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClosableDerivedReader } from '../lib/derived/derivedReader';
import { liveContent, type ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { shelfFile } from '../test/shelfFiles';
import { useDerivedReader } from './useDerivedReader';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const SOURCE = liveContent('docs');

const PHOTO = shelfFile('f', { sha256: 'abc' });

describe('a reader of what plugins made from a file', () => {
  let root: Root;
  let reader: ClosableDerivedReader | null;

  function Probe({ file, source, version }: { file: ShelfFile | null; source: ContentSource; version?: string }) {
    reader = useDerivedReader(file, source, version);
    return null;
  }

  async function show(file: ShelfFile | null, source = SOURCE, version?: string): Promise<ClosableDerivedReader | null> {
    await act(async () => root.render(<Probe file={file} source={source} version={version} />));
    return reader;
  }

  beforeEach(() => {
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => act(() => root.unmount()));

  it('is none for no file', async () => {
    expect(await show(null)).toBeNull();
  });

  it('stays the same as the file is listed anew, and knows what was made since', async () => {
    const first = (await show(PHOTO))!;
    expect(first.has('thumbnail')).toBe(false);

    expect(await show({ ...PHOTO, path: '/renamed', derived: { thumbnail: 'images-1' } })).toBe(first);
    expect(first.has('thumbnail')).toBe(true);
  });

  it('is another from another source, and for another version', async () => {
    const first = await show(PHOTO);
    const elsewhere = liveContent('other');

    const second = await show(PHOTO, elsewhere);
    expect(second).not.toBe(first);
    const third = await show(PHOTO, elsewhere, 'images-2');
    expect(third).not.toBe(second);
    expect(await show({ ...PHOTO, path: '/renamed' }, elsewhere, 'images-2')).toBe(third);
  });

  it('is another for other contents, and the one before is closed, as the last is once it goes', async () => {
    const first = (await show(PHOTO))!;
    const closedFirst = vi.spyOn(first, 'close');

    const second = (await show({ ...PHOTO, sha256: 'other' }))!;
    expect(second).not.toBe(first);
    expect(closedFirst).toHaveBeenCalledOnce();

    const closedSecond = vi.spyOn(second, 'close');
    await show(null);
    expect(closedSecond).toHaveBeenCalledOnce();
  });
});
