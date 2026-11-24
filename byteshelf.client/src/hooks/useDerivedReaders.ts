import { useEffect, useRef, useState } from 'react';
import { derivedReader, type ClosableDerivedReader } from '../lib/derived/derivedReader';
import { contentsKey } from '../lib/preview/fileContents';
import type { ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';

/** A file whose derived files are read, from `source`, and `version`, which a new reader is made for once it changes. */
export interface DerivedOf {
  file: ShelfFile;
  source: ContentSource;
  version?: string;
}

interface Held {
  key: string;
  source: ContentSource;
  reader: ClosableDerivedReader;
}

/**
 * A reader of what plugins made from each file's contents; null for no file. A file keeps its reader while it is any of
 * `files` with the same contents, source and version, though it is renamed, listed anew or moves to another place, as
 * when the file beside the one previewed becomes the one previewed, so what was read of it is not read again: a
 * blob-backed reader holds what it read, where one that answers addresses leaves it to the browser's cache. A reader no
 * file has any more is closed, as all are once this goes.
 */
export function useDerivedReaders(files: readonly (DerivedOf | null | undefined)[]): (ClosableDerivedReader | null)[] {
  const [held, setHeld] = useState<readonly Held[]>([]);
  const kept: Held[] = [];
  const readers = files.map((each) => {
    if (!each) return null;
    const key = `${contentsKey(each.file)} ${each.version ?? ''}`;
    const same = (other: Held) => other.key === key && other.source === each.source;
    let found = kept.find(same) ?? held.find(same);
    if (!found) found = { key, source: each.source, reader: derivedReader(each.file, each.source) };
    if (!kept.includes(found)) kept.push(found);
    return found.reader;
  });
  // Drawn again at once, before anything sees readers that are not kept.
  if (kept.length !== held.length || kept.some((each, index) => each !== held[index])) setHeld(kept);

  const open = useRef(new Set<ClosableDerivedReader>());
  useEffect(() => {
    for (const reader of open.current) {
      if (held.some((each) => each.reader === reader)) continue;
      reader.close();
      open.current.delete(reader);
    }
    held.forEach((each) => open.current.add(each.reader));
  }, [held]);
  useEffect(() => {
    const readers = open.current;
    return () => {
      readers.forEach((reader) => reader.close());
      readers.clear();
    };
  }, []);
  useEffect(() => {
    files.forEach((each, index) => each && readers[index]?.update(each.file));
  });

  return readers;
}
