import { useEffect, useMemo, useState } from 'react';
import { blobReader, sourceReader, type ContentReader } from '../lib/preview/contentReader';
import { isBlobBacked, type FileReading } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { useLoaded } from './useLoaded';

/** A blob-backed file up to this size is read into the page at once, a larger one only when asked. */
const READ_UNASKED_BYTES = 100 * 1024 * 1024;

/** Where a previewed file's contents are, on the way to its viewer. */
export type PreviewContents =
  | { status: 'ready'; reader: ContentReader; /** A blob-backed file's whole contents. */ held: Blob | null }
  | { status: 'reading' }
  /** Large enough that reading it into the page waits to be asked. */
  | { status: 'waiting'; read: () => void }
  /** Encrypted, and no password was given. */
  | { status: 'locked'; unlock: () => void }
  | { status: 'failed'; error: unknown; retry: () => void };

/** Tells one file's contents from another's: the same file renamed or described keeps them. */
export function contentsKey(file: ShelfFile): string {
  return `${file.id} ${file.sha256}`;
}

/**
 * A previewed file's contents: read by its address or, blob-backed, whole into the page first. What is
 * being read is aborted, and what was read let go, once another file's are previewed or none are.
 */
export function usePreviewContents(file: ShelfFile | undefined, { source, contents }: FileReading): PreviewContents {
  const key = file ? contentsKey(file) : null;
  const blobBacked = file !== undefined && isBlobBacked(file);
  const [asked, setAsked] = useState<string | null>(null);
  const waiting = blobBacked && file.sizeBytes > READ_UNASKED_BYTES && asked !== key;
  const loaded = useLoaded(blobBacked && !waiting ? key : null, (signal) => contents(file!, signal));

  const fileId = file?.id;
  const held = blobBacked && loaded.status === 'done' ? loaded.value : null;
  const reader = useMemo(() => {
    if (fileId === undefined) return null;
    if (!blobBacked) return sourceReader(fileId, source);
    return held && blobReader(held);
  }, [fileId, blobBacked, source, held]);
  useEffect(() => () => reader?.close(), [reader]);

  if (reader) return { status: 'ready', reader, held };
  if (waiting) return { status: 'waiting', read: () => setAsked(key) };
  if (loaded.status === 'failed') return { status: 'failed', error: loaded.error, retry: loaded.reload };
  if (loaded.status === 'done') return { status: 'locked', unlock: loaded.reload };
  return { status: 'reading' };
}
