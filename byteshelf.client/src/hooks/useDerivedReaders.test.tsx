// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClosableDerivedReader } from '../lib/derived/derivedReader';
import { liveContent } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { shelfFile } from '../test/shelfFiles';
import { useDerivedReaders, type DerivedOf } from './useDerivedReaders';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const SOURCE = liveContent('docs');

const readable = (file: ShelfFile | null): DerivedOf | null => file && { file, source: SOURCE };

describe('readers of what plugins made from the files a preview goes through', () => {
  let root: Root;
  let readers: (ClosableDerivedReader | null)[];

  function Probe({ files }: { files: (ShelfFile | null)[] }) {
    readers = useDerivedReaders(files.map(readable));
    return null;
  }

  async function show(...files: (ShelfFile | null)[]): Promise<(ClosableDerivedReader | null)[]> {
    await act(async () => root.render(<Probe files={files} />));
    return readers;
  }

  beforeEach(() => {
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => act(() => root.unmount()));

  it('keeps a file\'s reader as it moves to another place, and gives another file of the same contents its own', async () => {
    const [a, b, none] = await show(shelfFile('a', { sha256: 'a' }), shelfFile('b', { sha256: 'b' }), null);
    expect(none).toBeNull();

    const [movedB, movedA, copy] = await show(shelfFile('b', { sha256: 'b' }), shelfFile('a', { sha256: 'a' }), shelfFile('copy', { sha256: 'a' }));
    expect(movedB).toBe(b);
    expect(movedA).toBe(a);
    expect(copy).not.toBe(a);
  });

  it('closes a reader no file has any more, and the rest as it goes', async () => {
    const [a, b] = (await show(shelfFile('a', { sha256: 'a' }), shelfFile('b', { sha256: 'b' }))) as ClosableDerivedReader[];
    const closedA = vi.spyOn(a, 'close');
    const closedB = vi.spyOn(b, 'close');

    await show(shelfFile('b', { sha256: 'b' }), shelfFile('c', { sha256: 'c' }));
    expect(closedA).toHaveBeenCalledOnce();
    expect(closedB).not.toHaveBeenCalled();

    await act(async () => root.render(null));
    expect(closedB).toHaveBeenCalledOnce();
  });
});
