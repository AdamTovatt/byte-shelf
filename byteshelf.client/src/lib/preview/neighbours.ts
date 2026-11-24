import type { ShelfFile } from '../../services/shelfService';

/** Where a previewed file stands among the files the preview goes through. */
export interface Neighbours {
  /** Its place, from 1. */
  position: number;
  count: number;
  previous: ShelfFile | null;
  next: ShelfFile | null;
}

/** A previewed file's place among the files beside it, and the moves to those before and after it. */
export interface Steps {
  position: number;
  count: number;
  /** Whether more files follow the ones counted. */
  more: boolean;
  toPrevious: (() => void) | null;
  toNext: (() => void) | null;
}

/** Where the file `id` stands among `files`; null when it is not one of them. */
export function neighboursOf(files: readonly ShelfFile[], id: string): Neighbours | null {
  const index = files.findIndex((file) => file.id === id);
  if (index === -1) return null;
  return { position: index + 1, count: files.length, previous: files[index - 1] ?? null, next: files[index + 1] ?? null };
}
