import { useEffect, useMemo } from 'react';
import { blobReader, sourceReader, type ContentReader } from '../lib/preview/contentReader';
import { isBlobBacked, type FileReading, type ReadFile } from '../services/contentService';
import { useWholeContents, type WholeContents } from './useWholeContents';

/** Where a previewed file's contents are, on the way to its viewer. */
export type PreviewContents = { status: 'ready'; reader: ContentReader } | Exclude<WholeContents, { status: 'read' }>;

/**
 * The contents of what is `previewed`: read by its address or, blob-backed, whole into the page first, as
 * it is saved and opened too with the `reading` answered here (`useWholeContents`), null for any other.
 */
export function usePreviewContents(previewed: ReadFile | null, asksPassword: () => boolean): { contents: PreviewContents; reading: FileReading | null } {
  const blobBacked = previewed !== null && isBlobBacked(previewed.file);
  const { whole, reading } = useWholeContents(blobBacked ? previewed : null, asksPassword);

  const held = whole.status === 'read' ? whole.contents : null;
  const source = previewed?.reading.source;
  const fileId = previewed?.file.id;
  const reader = useMemo(() => {
    if (fileId === undefined || source === undefined) return null;
    if (!blobBacked) return sourceReader(fileId, source);
    return held && blobReader(held);
  }, [fileId, blobBacked, source, held]);
  useEffect(() => () => reader?.close(), [reader]);

  return { contents: reader ? { status: 'ready', reader } : whole.status === 'read' ? { status: 'reading' } : whole, reading };
}
