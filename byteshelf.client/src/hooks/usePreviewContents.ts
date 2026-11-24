import { useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import { blobReader, sourceReader, type ContentReader } from '../lib/preview/contentReader';
import { FileContents, contentsKey, type FileContentsState } from '../lib/preview/fileContents';
import { isBlobBacked, type ReadFile } from '../services/contentService';

/** Where a previewed file's contents are, on the way to its viewer. */
export type PreviewContents =
  | { status: 'ready'; reader: ContentReader }
  | { status: 'reading' }
  | { status: 'waiting'; read: () => void }
  | { status: 'locked'; error: unknown; unlock: () => void }
  | { status: 'failed'; error: unknown; retry: () => void };

/**
 * The contents of what is `previewed`: read by its address or, blob-backed, whole into the page first
 * (`FileContents`), which `whole` then answers to whatever saves or opens the file; null for any other. A
 * locked file's password is asked for as it shows when `asksPassword`.
 */
export function usePreviewContents(previewed: ReadFile | null, asksPassword: boolean): { contents: PreviewContents; whole: (() => Promise<Blob | null>) | null } {
  const whole = useFileContents(previewed, asksPassword);
  const state = useSyncExternalStore(whole?.subscribe ?? neverChanges, whole?.state ?? noState);

  const blobBacked = whole !== null;
  const held = state?.status === 'read' ? state.contents : null;
  const source = previewed?.reading.source;
  const fileId = previewed?.file.id;
  const reader = useMemo(() => {
    if (fileId === undefined || source === undefined) return null;
    if (!blobBacked) return sourceReader(fileId, source);
    return held && blobReader(held);
  }, [fileId, blobBacked, source, held]);
  useEffect(() => () => reader?.close(), [reader]);

  return { contents: shown(), whole: whole && (() => whole.contents()) };

  function shown(): PreviewContents {
    if (reader) return { status: 'ready', reader };
    if (state?.status === 'waiting') return { status: 'waiting', read: () => whole?.load() };
    if (state?.status === 'locked') return { status: 'locked', error: state.error, unlock: () => whole?.unlock() };
    if (state?.status === 'unlocking') return { status: 'locked', error: null, unlock: () => whole?.unlock() };
    if (state?.status === 'failed') return { status: 'failed', error: state.error, retry: () => whole?.load() };
    return { status: 'reading' };
  }
}

/** The whole contents of what is previewed, open while it is; null for what is not blob-backed. */
function useFileContents(previewed: ReadFile | null, asksPassword: boolean): FileContents | null {
  const key = previewed && isBlobBacked(previewed.file) ? contentsKey(previewed.file) : null;
  const [made, setMade] = useState<{ key: string | null; whole: FileContents | null }>({ key: null, whole: null });
  if (made.key !== key) setMade({ key, whole: key === null ? null : new FileContents(previewed!) });
  const { whole } = made;

  useEffect(() => {
    if (previewed) whole?.update(previewed);
  });
  useEffect(() => {
    whole?.open(asksPassword);
    return () => whole?.close();
  }, [whole, asksPassword]);

  return whole;
}

function neverChanges(): () => void {
  return () => {};
}

function noState(): FileContentsState | null {
  return null;
}
