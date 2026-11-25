import type { ClosableDerivedReader } from '../lib/derived/derivedReader';
import type { ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { useDerivedReaders } from './useDerivedReaders';

/**
 * A reader of what plugins made from the file's contents, which `source` gives: the same while they are, though
 * the file is renamed or listed anew, and while `version` is; null for no file.
 */
export function useDerivedReader(file: ShelfFile | null | undefined, source: ContentSource | undefined, version?: string): ClosableDerivedReader | null {
  return useDerivedReaders([file && source ? { file, source, version } : null])[0];
}
