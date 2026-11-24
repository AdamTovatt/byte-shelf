import { useCallback, useEffect, useMemo, useRef } from 'react';
import { openFile } from '../components/fileOpening';
import { neighboursOf, type Steps } from '../lib/preview/neighbours';
import type { PagePreview } from '../lib/preview/shownPreview';
import { runOrToast } from '../lib/utils';
import { isNotFound } from '../services/apiClient';
import { openable, type FileReading } from '../services/fileReading';
import type { ShelfFile } from '../services/shelfService';
import { usePluginsSettled } from './usePluginHost';
import { useCloseWhenGone, usePreviewParam } from './usePreviewParam';
import { useFile } from './useShelfQueries';
import { useViewers } from './useViewers';

/** The files a shelf page shows, which its preview goes through. */
export interface ShownFiles {
  files: ShelfFile[];
  /** Given while more follow, as a search's next page: fetches them. */
  fetchMore?: () => Promise<FetchedFiles>;
}

/** All the files shown once more were fetched, and whether more still follow. */
export interface FetchedFiles {
  files: ShelfFile[];
  more: boolean;
}

export interface ShelfPreview extends PagePreview {
  /** Whether a file opens in the preview: a viewer shows it in this browser, or one of a plugin still loading may. */
  showsInPreview: (file: ShelfFile) => boolean;
  /** Opens a file in the preview, or in a new tab when it does not open there. */
  open: (file: ShelfFile) => void;
}

/** The preview over a shelf's page, of one of the files `shown` there or, alone, of any other live one, read by `reading`. */
export function useShelfPreview(shelf: string, shown: ShownFiles, reading: FileReading): ShelfPreview {
  const { id, open, moveTo, close, wasOpened } = usePreviewParam();
  const listed = shown.files.find((file) => file.id === id);
  const { data: file, error, isFetching } = useFile(shelf, id, listed);

  const viewers = useViewers();
  const pluginsSettled = usePluginsSettled();
  const takes = useCallback((each: ShelfFile) => viewers.viewerFor(each) !== null, [viewers]);
  const previewable = useMemo(() => shown.files.filter(takes), [shown.files, takes]);

  // A file that was gone is asked for again as it is back: gone only once that has been answered.
  const gone = isNotFound(error) && !isFetching;
  useCloseWhenGone(gone, close);

  const current = useRef(id);
  useEffect(() => {
    current.current = id;
  }, [id]);

  /** Moves on to the next file a viewer takes, fetching more until there is one, or no more follow. */
  function moveFurther(fetchMore: () => Promise<FetchedFiles>): Promise<void> {
    return runOrToast(async () => {
      let fetched: FetchedFiles;
      do {
        fetched = await fetchMore();
        // Moved on or closed meanwhile, the answer is no longer wanted.
        if (current.current !== id) return;
        const next = neighboursOf(fetched.files.filter(takes), id!)?.next;
        if (next) return moveTo(next.id);
      } while (fetched.more);
    }, 'Could not fetch more files');
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

  function showsInPreview(file: ShelfFile): boolean {
    return Boolean(viewers.viewerFor(file)?.viewer) || !pluginsSettled;
  }

  return {
    isOpen: id !== null && !gone,
    showsInPreview,
    open: (opened) => {
      if (showsInPreview(opened)) open(opened.id);
      else void openFile(openable({ file: opened, reading }));
    },
    shown: {
      previewed: file ? { file, reading } : null,
      error,
      steps,
      onClose: close,
      asksPassword: wasOpened,
    },
  };
}
