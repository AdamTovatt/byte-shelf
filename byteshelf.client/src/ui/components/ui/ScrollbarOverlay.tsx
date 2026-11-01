import type { RefObject } from 'react';
import { cn } from '../../lib/utils';
import { useScrollbarOverlay } from '../../hooks/useScrollbarOverlay';
import { overlayHidePendingAttributes } from '../../hooks/scrollOverlay';
import { ScrollThumb } from './ScrollThumb';

/**
 * Bottom padding a scroll container's CONTENT needs so the horizontal track stops covering its last
 * row. The track floats over the viewport rather than taking layout space, so without this the final
 * row sits underneath it.
 *
 * Published here because this package owns the track's geometry: `.scrollbar-overlay-track-h` in
 * `styles.css` occupies the bottom 11px (`bottom: 3px` + `height: 8px`), and `pb-3` (12px) clears it.
 * A consumer computing that for itself would silently stop clearing the track the moment either value
 * changed — `scrollbarOverlayGutter.test.ts` in this package pins the two against each other.
 */
export const HORIZONTAL_SCROLLBAR_GUTTER = 'pb-3';

export interface ScrollbarOverlayProps {
  scrollRef: RefObject<HTMLElement | null>;
  /** Offset the vertical scrollbar track from the top (e.g. to clear a sticky header) */
  verticalTopOffset?: number;
  /**
   * Which tracks to draw. `'horizontal'` is for a scroller that already has a vertical track of its
   * own — a `CustomScroll`, which is vertical-only — and gains a sideways one here. The corner gap is
   * kept either way, since the other track is still drawn in that corner.
   */
  axes?: 'both' | 'horizontal';
  /**
   * Shows each drawn track for as long as its axis overflows, rather than only while the pointer is
   * over the scroller or it is scrolling. For content that overflows only sometimes and has to be
   * found when it does: a track that appears on hover tells nobody a mouse cannot see the rest.
   */
  persistent?: boolean;
}

export function ScrollbarOverlay({
  scrollRef,
  verticalTopOffset = 0,
  axes = 'both',
  persistent = false,
}: ScrollbarOverlayProps) {
  const {
    vTrackRef,
    hTrackRef,
    hasVScroll,
    hasHScroll,
    isVisible,
    isHidePending,
    vHandleTop,
    vHandleHeight,
    hHandleLeft,
    hHandleWidth,
    handleVMouseDown,
    handleHMouseDown,
    handleVTrackClick,
    handleHTrackClick,
  } = useScrollbarOverlay(scrollRef, verticalTopOffset);
  const shown = isVisible || persistent;
  // Reported on a persistent track too, although its hide changes nothing drawn: the attribute states
  // the hook's timer and nothing else, which is what lets a spec wait on it to know the timer has fired.
  const hidePendingAttributes = overlayHidePendingAttributes(isHidePending);

  return (
    <>
      {hasVScroll && axes === 'both' && (
        <div
          ref={vTrackRef}
          className={cn('scrollbar-overlay-track scrollbar-overlay-track-v', shown && 'scrollbar-overlay-track-visible')}
          {...hidePendingAttributes}
          style={verticalTopOffset ? { top: verticalTopOffset } : undefined}
          onClick={handleVTrackClick}
        >
          <div
            className="scrollbar-overlay-handle scrollbar-overlay-handle-v"
            style={{ top: vHandleTop, height: vHandleHeight }}
            onMouseDown={handleVMouseDown}
            onClick={(e) => e.stopPropagation()}
          >
            <ScrollThumb />
          </div>
        </div>
      )}
      {hasHScroll && (
        <div
          ref={hTrackRef}
          className={cn('scrollbar-overlay-track scrollbar-overlay-track-h', shown && 'scrollbar-overlay-track-visible')}
          {...hidePendingAttributes}
          onClick={handleHTrackClick}
        >
          <div
            className="scrollbar-overlay-handle scrollbar-overlay-handle-h"
            style={{ left: hHandleLeft, width: hHandleWidth }}
            onMouseDown={handleHMouseDown}
            onClick={(e) => e.stopPropagation()}
          >
            <ScrollThumb />
          </div>
        </div>
      )}
    </>
  );
}
