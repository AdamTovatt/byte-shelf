import { type RefObject, useCallback, useEffect, useRef, useState } from 'react';
import {
  type ScrollAxisGeometry,
  computeHandleMetrics,
  computeHandleSize,
  handlePositionForTrackClick,
  scrollForHandlePosition,
} from './scrollOverlay';
import { useOverlayHideTimer } from './useOverlayHideTimer';
import { useSupportsHover } from './useSupportsHover';
import { type ScrollContentMetrics, useScrollContentMetrics } from './useScrollContentMetrics';

/** Reserved space (px) where vertical and horizontal scrollbar tracks would overlap at the corner */
const CORNER_GAP = 11;

/** Compute the usable length of the vertical scrollbar track */
function getVTrackLength(viewHeight: number, verticalTopOffset: number, bothVisible: boolean): number {
  return Math.max(0, viewHeight - verticalTopOffset - (bothVisible ? CORNER_GAP : 0));
}

/** Compute the usable length of the horizontal scrollbar track */
function getHTrackLength(viewWidth: number, bothVisible: boolean): number {
  return Math.max(0, viewWidth - (bothVisible ? CORNER_GAP : 0));
}

/** Narrow the measured viewport and content down to the one axis being acted on */
function getAxisGeometry(
  metrics: ScrollContentMetrics,
  axis: 'v' | 'h',
  verticalTopOffset: number,
): ScrollAxisGeometry {
  const bothVisible = metrics.contentHeight > metrics.viewHeight && metrics.contentWidth > metrics.viewWidth;
  if (axis === 'v') {
    return {
      trackLength: getVTrackLength(metrics.viewHeight, verticalTopOffset, bothVisible),
      viewSize: metrics.viewHeight,
      contentSize: metrics.contentHeight,
    };
  }
  return {
    trackLength: getHTrackLength(metrics.viewWidth, bothVisible),
    viewSize: metrics.viewWidth,
    contentSize: metrics.contentWidth,
  };
}

export interface UseScrollbarOverlayResult {
  vTrackRef: RefObject<HTMLDivElement | null>;
  hTrackRef: RefObject<HTMLDivElement | null>;
  hasVScroll: boolean;
  hasHScroll: boolean;
  isVisible: boolean;
  /** Whether the fade-out timer is pending; spread `overlayHidePendingAttributes` of it onto each track */
  isHidePending: boolean;
  vHandleTop: number;
  vHandleHeight: number;
  hHandleLeft: number;
  hHandleWidth: number;
  handleVMouseDown: (event: React.MouseEvent) => void;
  handleHMouseDown: (event: React.MouseEvent) => void;
  handleVTrackClick: (event: React.MouseEvent) => void;
  handleHTrackClick: (event: React.MouseEvent) => void;
}

/**
 * Overlay scrollbars for a scroll element the caller already owns, on both axes: viewport/content
 * metrics, handle geometry, reveal-on-scroll, hover-gated reveal (see {@link useSupportsHover}),
 * handle drag, track-click page-jump, and the fade-out timer (see {@link useOverlayHideTimer}). The
 * consumer renders the tracks and handles, attaches the returned refs, and wires the returned
 * handlers onto them.
 *
 * This is the two-axis, element-decorating sibling of {@link useVerticalScrollOverlay}, which owns
 * its own scroll container and handles a single axis. Both take their handle geometry and design
 * constants from the shared primitives in `./scrollOverlay`; the corner gap where the two tracks
 * would meet is this hook's alone.
 */
export function useScrollbarOverlay(
  scrollRef: RefObject<HTMLElement | null>,
  verticalTopOffset: number = 0,
): UseScrollbarOverlayResult {
  const vTrackRef = useRef<HTMLDivElement>(null);
  const hTrackRef = useRef<HTMLDivElement>(null);

  const supportsHover = useSupportsHover();

  // Viewport-vs-content measurement is shared with everything else that has to know whether a track is
  // on screen (see useScrollContentMetrics) — this hook only turns it into handle geometry.
  const {
    metricsRef,
    hasVerticalOverflow: hasVScroll,
    hasHorizontalOverflow: hasHScroll,
    measurementVersion,
  } = useScrollContentMetrics(scrollRef);

  const [visible, setVisible] = useState(false);
  const [draggingAxis, setDraggingAxis] = useState<'v' | 'h' | null>(null);
  const draggingAxisRef = useRef<'v' | 'h' | null>(null);

  // Vertical handle state
  const [vHandleTop, setVHandleTop] = useState(0);
  const [vHandleHeight, setVHandleHeight] = useState(0);

  // Horizontal handle state
  const [hHandleLeft, setHHandleLeft] = useState(0);
  const [hHandleWidth, setHHandleWidth] = useState(0);

  const dragStartPos = useRef(0);
  const dragStartHandlePos = useRef(0);

  // Keep ref in sync so non-reactive listeners can read it without re-subscribing
  useEffect(() => {
    draggingAxisRef.current = draggingAxis;
  }, [draggingAxis]);

  const hideScrollbar = useCallback(() => setVisible(false), []);
  const { isHidePending, scheduleHide, clearHideTimer } = useOverlayHideTimer(hideScrollbar);

  const computeHandles = useCallback((scrollLeft: number, scrollTop: number) => {
    const metrics = metricsRef.current;
    const { viewWidth, viewHeight, contentWidth, contentHeight } = metrics;
    const bothVisible = contentHeight > viewHeight && contentWidth > viewWidth;

    if (contentHeight > viewHeight) {
      const trackLength = getVTrackLength(viewHeight, verticalTopOffset, bothVisible);
      const { handleSize, handlePos } = computeHandleMetrics(
        { viewSize: viewHeight, contentSize: contentHeight, trackLength },
        scrollTop,
      );
      setVHandleTop(handlePos);
      setVHandleHeight(handleSize);
    }

    if (contentWidth > viewWidth) {
      const trackLength = getHTrackLength(viewWidth, bothVisible);
      const { handleSize, handlePos } = computeHandleMetrics(
        { viewSize: viewWidth, contentSize: contentWidth, trackLength },
        scrollLeft,
      );
      setHHandleLeft(handlePos);
      setHHandleWidth(handleSize);
    }
  }, [verticalTopOffset, metricsRef]);

  // Re-place the handles against the latest measurement. `measurementVersion` is the trigger because
  // the metrics themselves mutate in place; `computeHandles` is in here too, so a change to
  // `verticalTopOffset` (which shortens the vertical track) also re-places them.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    computeHandles(el.scrollLeft, el.scrollTop);
  }, [scrollRef, computeHandles, measurementVersion]);

  // Listen to scroll events on the scroll element
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;

    const onScroll = () => {
      computeHandles(el.scrollLeft, el.scrollTop);
      setVisible(true);
      scheduleHide();
    };

    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, [scrollRef, computeHandles, scheduleHide]);

  // No touchstart reveal: revealing the scrollbar during a tap makes iOS Safari swallow that tap's
  // click. It still reveals on the real scroll event above — the case that matters on touch.

  // Hover detection on parent element
  useEffect(() => {
    if (!supportsHover) return;

    const el = scrollRef.current;
    if (!el) return;
    const parent = el.parentElement;
    if (!parent) return;

    const onEnter = () => {
      clearHideTimer();
      setVisible(true);
    };
    const onLeave = () => {
      if (!draggingAxisRef.current) {
        scheduleHide();
      }
    };

    parent.addEventListener('mouseenter', onEnter);
    parent.addEventListener('mouseleave', onLeave);
    return () => {
      parent.removeEventListener('mouseenter', onEnter);
      parent.removeEventListener('mouseleave', onLeave);
    };
  }, [supportsHover, scrollRef, clearHideTimer, scheduleHide]);

  // ── Handle drag ──
  const startHandleDrag = useCallback((axis: 'v' | 'h', event: React.MouseEvent) => {
    event.preventDefault();
    event.stopPropagation();
    dragStartPos.current = axis === 'v' ? event.pageY : event.pageX;
    dragStartHandlePos.current = axis === 'v' ? vHandleTop : hHandleLeft;
    setDraggingAxis(axis);
    clearHideTimer();
  }, [vHandleTop, hHandleLeft, clearHideTimer]);

  const handleVMouseDown = useCallback((e: React.MouseEvent) => startHandleDrag('v', e), [startHandleDrag]);
  const handleHMouseDown = useCallback((e: React.MouseEvent) => startHandleDrag('h', e), [startHandleDrag]);

  // Global mouse move/up for drag
  useEffect(() => {
    if (!draggingAxis) return;

    const onMouseMove = (event: MouseEvent) => {
      event.preventDefault();
      const el = scrollRef.current;
      if (!el) return;

      const delta = draggingAxis === 'v' ? event.pageY - dragStartPos.current : event.pageX - dragStartPos.current;
      const axisGeometry = getAxisGeometry(metricsRef.current, draggingAxis, verticalTopOffset);
      const scrollValue = scrollForHandlePosition(axisGeometry, dragStartHandlePos.current + delta);
      if (scrollValue === null) return;

      if (draggingAxis === 'v') {
        el.scrollTop = scrollValue;
      } else {
        el.scrollLeft = scrollValue;
      }
    };

    const onMouseUp = (event: MouseEvent) => {
      event.preventDefault();
      setDraggingAxis(null);
      scheduleHide();
    };

    document.addEventListener('mousemove', onMouseMove, { passive: false });
    document.addEventListener('mouseup', onMouseUp, { passive: false });

    return () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
    };
  }, [draggingAxis, scrollRef, scheduleHide, verticalTopOffset, metricsRef]);

  // ── Track click — page jump ──
  const handleTrackClick = useCallback((axis: 'v' | 'h', event: React.MouseEvent) => {
    const el = scrollRef.current;
    const track = axis === 'v' ? vTrackRef.current : hTrackRef.current;
    if (!el || !track) return;

    const trackRect = track.getBoundingClientRect();
    const clickPos = axis === 'v' ? event.clientY - trackRect.top : event.clientX - trackRect.left;
    const axisGeometry = getAxisGeometry(metricsRef.current, axis, verticalTopOffset);
    const handlePos = axis === 'v' ? vHandleTop : hHandleLeft;

    const newPos = handlePositionForTrackClick(clickPos, handlePos, computeHandleSize(axisGeometry));
    if (newPos === null) return;

    const scrollValue = scrollForHandlePosition(axisGeometry, newPos);
    if (scrollValue === null) return;

    if (axis === 'v') {
      el.scrollTop = scrollValue;
    } else {
      el.scrollLeft = scrollValue;
    }
  }, [scrollRef, verticalTopOffset, vHandleTop, hHandleLeft, metricsRef]);

  const handleVTrackClick = useCallback((e: React.MouseEvent) => handleTrackClick('v', e), [handleTrackClick]);
  const handleHTrackClick = useCallback((e: React.MouseEvent) => handleTrackClick('h', e), [handleTrackClick]);

  const isVisible = visible || draggingAxis !== null;

  return {
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
  };
}
