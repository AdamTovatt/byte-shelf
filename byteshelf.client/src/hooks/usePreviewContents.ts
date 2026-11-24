import { useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { blobReader, sourceReader, type ContentReader } from '../lib/preview/contentReader';
import { FileContents, contentsKey, type FileContentsState } from '../lib/preview/fileContents';
import { isBlobBacked } from '../services/contentService';
import type { ReadableFile } from '../services/fileReading';

/** Where a previewed file's contents are, on the way to its viewer. */
export type PreviewContents =
  | { status: 'ready'; reader: ContentReader }
  | { status: 'reading' }
  | { status: 'waiting'; read: () => void }
  | { status: 'locked'; error: unknown; unlock: () => void }
  | { status: 'failed'; error: unknown; retry: () => void };

/**
 * The contents of what is `previewed`: read by its address or, blob-backed, whole into the page first
 * (`FileContents`), which `readWhole` then answers to whatever saves or opens the file; null for any other. A
 * locked file's password is asked for as it shows when `asksPassword`.
 */
export function usePreviewContents(previewed: ReadableFile | null, asksPassword: boolean): { contents: PreviewContents; readWhole: (() => Promise<Blob | null>) | null } {
  const fileContents = useFileContents(previewed, asksPassword);
  const state = useSyncExternalStore(fileContents?.subscribe ?? neverChanges, fileContents?.state ?? noState);

  const blobBacked = fileContents !== null;
  const held = state?.status === 'read' ? state.contents : null;
  const source = previewed?.reading.source;
  const fileId = previewed?.file.id;
  const reader = useMemo(() => {
    if (fileId === undefined || source === undefined) return null;
    if (!blobBacked) return sourceReader(fileId, source);
    return held && blobReader(held);
  }, [fileId, blobBacked, source, held]);
  useEffect(() => () => reader?.close(), [reader]);

  return { contents: shown(), readWhole: fileContents && (() => fileContents.whole()) };

  function shown(): PreviewContents {
    if (reader) return { status: 'ready', reader };
    if (state?.status === 'waiting') return { status: 'waiting', read: () => fileContents?.load() };
    if (state?.status === 'locked') return { status: 'locked', error: state.error, unlock: () => fileContents?.unlock() };
    if (state?.status === 'unlocking') return { status: 'locked', error: null, unlock: () => fileContents?.unlock() };
    if (state?.status === 'failed') return { status: 'failed', error: state.error, retry: () => fileContents?.load() };
    return { status: 'reading' };
  }
}

/** The whole contents of what is previewed, open while it is; null for what is not blob-backed. */
function useFileContents(previewed: ReadableFile | null, asksPassword: boolean): FileContents | null {
  const key = previewed && isBlobBacked(previewed.file) ? contentsKey(previewed.file) : null;
  const [made, setMade] = useState<{ key: string | null; fileContents: FileContents | null }>({ key: null, fileContents: null });
  if (made.key !== key) setMade({ key, fileContents: key === null ? null : new FileContents(previewed!) });
  const { fileContents } = made;

  useEffect(() => {
    if (previewed) fileContents?.update(previewed);
  });
  useEffect(() => {
    fileContents?.open(asksPassword);
    return () => fileContents?.close();
  }, [fileContents, asksPassword]);

  return fileContents;
}

function neverChanges(): () => void {
  return () => {};
}

function noState(): FileContentsState | null {
  return null;
}
