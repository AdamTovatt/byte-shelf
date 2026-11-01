import { useCallback, useEffect, useRef, useState } from 'react';
import { FADE_OUT_DELAY_MS } from './scrollOverlay';

export interface OverlayHideTimer {
  /**
   * Whether a hide is scheduled and has not yet fired or been cancelled. A track spreads
   * `overlayHidePendingAttributes` of it, which is what the e2e capture settle waits on.
   */
  isHidePending: boolean;
  /** (Re)starts the {@link FADE_OUT_DELAY_MS} countdown to `onHide` */
  scheduleHide: () => void;
  /** Cancels a pending hide, if there is one */
  clearHideTimer: () => void;
}

/**
 * The fade-out timer both overlay scrollbar hooks share, and the "hide pending" mark that reports it.
 *
 * One home for the rule the mark depends on: it is set beside the timer when the hide is scheduled,
 * cleared beside it when the hide is cancelled, and cleared in the same callback that runs `onHide`, so
 * the render that hides the track is the render that drops the mark. A second timer, or the mark kept
 * anywhere else, could disagree with the fade it reports.
 *
 * Separate from `./scrollOverlay` because that module stays free of React: the e2e harness imports its
 * constants into Playwright's runner.
 *
 * `onHide` must be stable across renders (a state setter, or a callback memoised on one), since a new
 * function each render would rebuild `scheduleHide` each render.
 */
export function useOverlayHideTimer(onHide: () => void): OverlayHideTimer {
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [isHidePending, setIsHidePending] = useState(false);

  const clearHideTimer = useCallback(() => {
    if (hideTimer.current) {
      clearTimeout(hideTimer.current);
      hideTimer.current = null;
      setIsHidePending(false);
    }
  }, []);

  const scheduleHide = useCallback(() => {
    clearHideTimer();
    hideTimer.current = setTimeout(() => {
      hideTimer.current = null;
      onHide();
      setIsHidePending(false);
    }, FADE_OUT_DELAY_MS);
    setIsHidePending(true);
  }, [clearHideTimer, onHide]);

  // Cleanup timer on unmount
  useEffect(() => {
    return () => clearHideTimer();
  }, [clearHideTimer]);

  return { isHidePending, scheduleHide, clearHideTimer };
}
