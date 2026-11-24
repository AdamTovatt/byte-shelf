import { useCallback, useEffect, useRef } from 'react';
import toast from 'react-hot-toast';
import { useLocation, useNavigate } from 'react-router-dom';

/** The query parameter naming what is previewed over a page. */
const PARAM = 'preview';

/** Kept with the history entry the page pushed to open a preview, so closing it goes back to the one before. */
interface PreviewState {
  previewOpenedHere: true;
}

const OPENED_HERE: PreviewState = { previewOpenedHere: true };

export interface PreviewParam {
  /** What is previewed; null when nothing is. */
  id: string | null;
  /** Opens a preview as a step in the history, so Back closes it; with one open already, moves it on. */
  open: (id: string) => void;
  /** Moves an open preview on to another, which Back closes all the same. */
  moveTo: (id: string) => void;
  close: () => void;
}

/** The preview in the page's URL (`?preview=<id>`), so a reload keeps it. */
export function usePreviewParam(): PreviewParam {
  const { search, state: entryState, key } = useLocation();
  const state = entryState as PreviewState | null;
  const navigate = useNavigate();
  const id = new URLSearchParams(search).get(PARAM);

  // Going back is not at once, so a second close of the same history entry before it would leave the
  // page behind the preview.
  const closing = useRef<string | null>(null);
  useEffect(() => {
    if (id === null) closing.current = null;
  }, [id]);

  const moveTo = useCallback((next: string) => navigate({ search: withPreview(search, next) }, { replace: true, state }), [navigate, search, state]);

  const open = useCallback((opened: string) => {
    if (id === null) navigate({ search: withPreview(search, opened) }, { state: OPENED_HERE });
    else moveTo(opened);
  }, [id, navigate, search, moveTo]);

  const close = useCallback(() => {
    if (closing.current === key) return;
    closing.current = key;
    if (state?.previewOpenedHere) navigate(-1);
    else navigate({ search: withPreview(search, null) }, { replace: true });
  }, [key, navigate, search, state]);

  return { id, open, moveTo, close };
}

/** Closes a preview, saying so, once what it shows is gone. */
export function useCloseWhenGone(gone: boolean, close: () => void): void {
  useEffect(() => {
    if (!gone) return;
    toast.error('That file is no longer here.', { id: 'preview-gone' });
    close();
  }, [gone, close]);
}

function withPreview(search: string, id: string | null): string {
  const query = new URLSearchParams(search);
  if (id === null) query.delete(PARAM);
  else query.set(PARAM, id);
  return query.toString();
}
