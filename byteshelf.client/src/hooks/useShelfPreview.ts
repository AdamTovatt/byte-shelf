import { useCallback, useEffect, useMemo, useRef } from 'react';
import { whyNotInPage } from '../components/preview/viewers/inPage';
import { neighboursOf, type Steps } from '../lib/preview/neighbours';
import { isNotFound } from '../services/apiClient';
import type { FileReading, ReadFile } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { useCloseWhenGone, usePreviewParam } from './usePreviewParam';
import { useFile } from './useShelfQueries';
import { useViewers } from './useViewers';

/** The files a shelf page shows, which its preview goes through. */
export interface ShownFiles {
  files: ShelfFile[];
  /** Given while more follow, as a search's next page: fetches them, answering all the files shown then. */
  fetchMore?: () => Promise<ShelfFile[]>;
}

export interface ShelfPreview {
  isOpen: boolean;
  /** Whether a file opens in the preview: a viewer takes it, and this browser shows it in the page. */
  showsInPreview: (file: ShelfFile) => boolean;
  open: (file: ShelfFile) => void;
  /** What the preview shows while it is open. */
  shown: {
    previewed: ReadFile | null;
    error: Error | null;
    steps: Steps | null;
    onClose: () => void;
    asksPassword: () => boolean;
  };
}

/** The preview over a shelf's page, of one of the files `shown` there or, alone, of any other live one, read by `reading`. */
export function useShelfPreview(shelf: string, shown: ShownFiles, reading: FileReading): ShelfPreview {
  const { id, open, moveTo, close, wasOpened } = usePreviewParam();
  const listed = shown.files.find((file) => file.id === id);
  const { data: file, error, isFetching } = useFile(shelf, id, listed);

  const viewers = useViewers();
  const takes = useCallback((each: ShelfFile) => viewers.viewerFor(each) !== null, [viewers]);
  const previewable = useMemo(() => shown.files.filter(takes), [shown.files, takes]);

  // A file that was gone is asked for again as it is back: gone only once that has been answered.
  const gone = isNotFound(error) && !isFetching;
  useCloseWhenGone(gone, close);

  const current = useRef(id);
  useEffect(() => {
    current.current = id;
  }, [id]);

  async function moveFurther(fetchMore: () => Promise<ShelfFile[]>): Promise<void> {
    const next = neighboursOf((await fetchMore()).filter(takes), id!)?.next;
    // Moved on or closed meanwhile, the answer is no longer wanted.
    if (next && current.current === id) moveTo(next.id);
  }

  const { fetchMore } = shown;
  const among = id === null ? null : neighboursOf(previewable, id);
  const steps: Steps | null = among && {
    position: among.position,
    count: among.count,
    more: fetchMore !== undefined,
    toPrevious: among.previous && (() => moveTo(among.previous!.id)),
    toNext: among.next ? () => moveTo(among.next!.id) : fetchMore ? () => void moveFurther(fetchMore) : null,
  };

  return {
    isOpen: id !== null && !gone,
    showsInPreview: (each) => {
      const viewer = viewers.viewerFor(each);
      return viewer !== null && whyNotInPage(viewer) === null;
    },
    open: (opened) => open(opened.id),
    shown: {
      previewed: file ? { file, reading } : null,
      error,
      steps,
      onClose: close,
      asksPassword: wasOpened,
    },
  };
}
