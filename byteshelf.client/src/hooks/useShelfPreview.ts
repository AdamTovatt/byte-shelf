import { useEffect, useMemo, useRef } from 'react';
import { viewers } from '../components/preview/viewers';
import { neighboursOf, type Neighbours } from '../lib/preview/neighbours';
import { isNotFound } from '../services/apiClient';
import type { ShelfFile } from '../services/shelfService';
import { useCloseWhenGone, usePreviewParam } from './usePreviewParam';
import { useFile } from './useShelfQueries';

/** The files a shelf page shows, which its preview goes through. */
export interface ShownFiles {
  files: ShelfFile[];
  /** Given while more follow, as a search's next page: fetches them, answering all the files shown then. */
  fetchMore?: () => Promise<ShelfFile[]>;
}

export interface ShelfPreview {
  isOpen: boolean;
  /** Whether a file opens in the preview: a viewer takes it. */
  takes: (file: ShelfFile) => boolean;
  open: (file: ShelfFile) => void;
  /** What the preview shows while it is open. */
  shown: {
    file: ShelfFile | undefined;
    error: Error | null;
    neighbours: Neighbours | null;
    onMoveTo: (file: ShelfFile) => void;
    onMoveFurther?: () => void;
    onClose: () => void;
  };
}

/** The preview over a shelf's page, of one of the files `shown` there or, alone, of any other live one. */
export function useShelfPreview(shelf: string, shown: ShownFiles): ShelfPreview {
  const { id, open, moveTo, close } = usePreviewParam();
  const listed = shown.files.find((file) => file.id === id);
  const { data: file, error, isFetching } = useFile(shelf, id, listed);
  const previewable = useMemo(() => shown.files.filter(takes), [shown.files]);

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
  const neighbours = id === null ? null : neighboursOf(previewable, id);
  return {
    isOpen: id !== null && !gone,
    takes,
    open: (opened) => open(opened.id),
    shown: {
      file,
      error,
      neighbours,
      onMoveTo: (next) => moveTo(next.id),
      onMoveFurther: neighbours && fetchMore ? () => void moveFurther(fetchMore) : undefined,
      onClose: close,
    },
  };
}

function takes(file: ShelfFile): boolean {
  return viewers.viewerFor(file) !== null;
}
