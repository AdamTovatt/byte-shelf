import { useEffect } from 'react';
import { derivedReader, type ClosableDerivedReader } from '../lib/derived/derivedReader';
import { contentsKey } from '../lib/preview/fileContents';
import type { ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { useMadeFor } from './useMadeFor';

/**
 * A reader of what plugins made from the file's contents, which `source` gives: the same while they are, though
 * the file is renamed or listed anew, and while `version` is; null for no file.
 */
export function useDerivedReader(file: ShelfFile | null | undefined, source: ContentSource | undefined, version?: string): ClosableDerivedReader | null {
  const reader = useMadeFor(file && source ? [contentsKey(file), source, version] : null, () => derivedReader(file!, source!));

  useEffect(() => {
    if (file) reader?.update(file);
  });
  useEffect(() => () => reader?.close(), [reader]);

  return reader;
}
