import { type RefObject, type UIEvent, useCallback, useEffect, useRef, useState } from 'react';
import {
  type ScrollAxisGeometry,
  computeHandleMetrics,
  computeHandleSize,
  handlePositionForTrackClick,
  scrollForHandlePosition,
} from './scrollOverlay';
import { useOverlayHideTimer } from './useOverlayHideTimer';
import { useSupportsHover } from './useSupportsHover';

export interface VerticalScrollOverlayOptions {
  /** Offset the scrollbar track from the top (e.g. to clear a sticky header) */
  scrollBarTopOffset?: number;
  /** Forwarded when the scroll container fires a native scroll event */
  onScroll?: (event: UIEvent) => void;
  /** Optional external ref that also receives the scroll container element */
  scrollContainerRef?: RefObject<HTMLDivElement | null>;
}

export interface VerticalScrollOverlayResult {
  /**
   * Callback ref for the scroll container — the element that owns and renders the scrollable
   * content. Also forwards the element to any external `scrollContainerRef` passed in the options.
   */
  setScrollContainer: (node: HTMLDivElement | null) => void;
  /** Attach to the immediate content wrapper inside the scroll container (observed for resize) */
  contentRef: RefObject<HTMLDivElement | null>;
  /** Attach to the scrollbar track element */
  trackRef: RefObject<HTMLDivElement | null>;
  /** Whether the content overflows and the custom scrollbar should render */
  hasScroll: boolean;
  /** Whether the scrollbar is currently revealed (native scroll, hover, or an active drag) */
  isVisible: boolean;
  /** Whether the fade-out timer is pending; spread `overlayHidePendingAttributes` of it onto the track */
  isHidePending: boolean;
  handleTop: number;
  handleHeight: number;
  onScroll: (event: UIEvent<HTMLDivElement>) => void;
  onMouseEnter: () => void;
  onMouseLeave: () => void;
  onTrackClick: (event: React.MouseEvent) => void;
  onHandleMouseDown: (event: React.MouseEvent) => void;
}

/**
 * Shared core for the self-contained vertical overlay scrollbar behind `CustomScroll` and
 * `OverflowScroll`: content/viewport metrics, handle geometry, reveal-on-native-scroll,
 * hover-gated reveal (see {@link useSupportsHover}), handle drag, track-click page-jump, and the
 * fade-out timer (see {@link useOverlayHideTimer}). The consumer owns the rendered DOM and class
 * contract; it attaches the returned refs and wires the returned handlers onto its own elements.
 *
 * This is the vertical-only, container-owning sibling of {@link useScrollbarOverlay}: this hook
 * provides (owns) the scroll container ref and handles a single axis, whereas `useScrollbarOverlay`
 * decorates an externally-owned scroll element across both axes. Both take their handle geometry and
 * design constants from the shared primitives in `./scrollOverlay`; each owns its own reveal/fade
 * state, its scroll and pointer wiring, and the track length it feeds the geometry.
 */
export function useVerticalScrollOverlay(
  options: VerticalScrollOverlayOptions = {},
): VerticalScrollOverlayResult {
  const { scrollBarTopOffset = 0, onScroll: onScrollProp, scrollContainerRef: externalScrollContainerRef } = options;

  const scrollContainerRef = useRef<HTMLDivElement | null>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const trackRef = useRef<HTMLDivElement>(null);

  const setScrollContainer = useCallback(
    (node: HTMLDivElement | null) => {
      scrollContainerRef.current = node;
      if (externalScrollContainerRef) {
        externalScrollContainerRef.current = node;
      }
    },
    [externalScrollContainerRef],
  );

  const supportsHover = useSupportsHover();

  const [hasScroll, setHasScroll] = useState(false);
  const [visible, setVisible] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [handleTop, setHandleTop] = useState(0);
  const [handleHeight, setHandleHeight] = useState(0);

  const dragStartMouseY = useRef(0);
  const dragStartHandleTop = useRef(0);
  const visibleHeight = useRef(0);
  const contentHeight = useRef(0);

  const hideScrollbar = useCallback(() => setVisible(false), []);
  const { isHidePending, scheduleHide, clearHideTimer } = useOverlayHideTimer(hideScrollbar);

  const computeMetrics = useCallback(() => {
    const inner = scrollContainerRef.current;
    if (!inner) return;

    const vHeight = inner.clientHeight;
    const cHeight = inner.scrollHeight;

    visibleHeight.current = vHeight;
    contentHeight.current = cHeight;

    setHasScroll(cHeight > vHeight);
  }, []);

  /** The measured axis the handle geometry is derived from, as of the last measurement */
  const readAxisGeometry = useCallback((): ScrollAxisGeometry => ({
    viewSize: visibleHeight.current,
    contentSize: contentHeight.current,
    trackLength: visibleHeight.current - scrollBarTopOffset,
  }), [scrollBarTopOffset]);

  const computeHandle = useCallback((scrollTop: number) => {
    const { handleSize, handlePos } = computeHandleMetrics(readAxisGeometry(), scrollTop);

    setHandleTop(handlePos);
    setHandleHeight(handleSize);
  }, [readAxisGeometry]);

  // Observe content size changes
  useEffect(() => {
    const inner = scrollContainerRef.current;
    const content = contentRef.current;
    if (!inner || !content) return;

    const update = () => {
      computeMetrics();
      computeHandle(inner.scrollTop);
    };

    update();

    const observer = new ResizeObserver(update);
    observer.observe(inner);
    observer.observe(content);

    return () => observer.disconnect();
  }, [computeMetrics, computeHandle]);

  // Handle native scroll
  const handleScroll = useCallback(
    (event: UIEvent<HTMLDivElement>) => {
      const target = event.currentTarget;
      computeMetrics();
      computeHandle(target.scrollTop);

      setVisible(true);
      scheduleHide();

      onScrollProp?.(event as UIEvent);
    },
    [computeMetrics, computeHandle, scheduleHide, onScrollProp],
  );

  // No touchstart reveal: revealing the scrollbar during a tap makes iOS Safari swallow that tap's
  // click. It still reveals on the real scroll event above — the case that matters on touch.

  // Mouse enter/leave on outer wrapper
  const handleMouseEnter = useCallback(() => {
    if (!supportsHover) return;
    clearHideTimer();
    setVisible(true);
  }, [supportsHover, clearHideTimer]);

  const handleMouseLeave = useCallback(() => {
    if (!supportsHover) return;
    if (!dragging) {
      scheduleHide();
    }
  }, [supportsHover, dragging, scheduleHide]);

  // Drag handling
  const handleMouseDown = useCallback(
    (event: React.MouseEvent) => {
      event.preventDefault();
      event.stopPropagation();
      dragStartMouseY.current = event.pageY;
      dragStartHandleTop.current = handleTop;
      setDragging(true);
      clearHideTimer();
    },
    [handleTop, clearHideTimer],
  );

  useEffect(() => {
    if (!dragging) return;

    const onMouseMove = (event: MouseEvent) => {
      event.preventDefault();
      const deltaY = event.pageY - dragStartMouseY.current;
      const newScrollTop = scrollForHandlePosition(readAxisGeometry(), dragStartHandleTop.current + deltaY);
      if (newScrollTop === null) return;

      if (scrollContainerRef.current) {
        scrollContainerRef.current.scrollTop = newScrollTop;
      }
    };

    const onMouseUp = (event: MouseEvent) => {
      event.preventDefault();
      setDragging(false);
      scheduleHide();
    };

    document.addEventListener('mousemove', onMouseMove, { passive: false });
    document.addEventListener('mouseup', onMouseUp, { passive: false });

    return () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
    };
  }, [dragging, scheduleHide, readAxisGeometry]);

  // Track click — page jump
  const handleTrackClick = useCallback(
    (event: React.MouseEvent) => {
      if (!trackRef.current || !scrollContainerRef.current) return;

      const trackRect = trackRef.current.getBoundingClientRect();
      const clickY = event.clientY - trackRect.top;
      const geometry = readAxisGeometry();
      const currentHandleTop = handleTop;
      const currentHandleHeight = computeHandleSize(geometry);

      const newHandleTop = handlePositionForTrackClick(clickY, currentHandleTop, currentHandleHeight);
      if (newHandleTop === null) return;

      const newScrollTop = scrollForHandlePosition(geometry, newHandleTop);
      if (newScrollTop === null) return;

      scrollContainerRef.current.scrollTop = newScrollTop;
    },
    [handleTop, readAxisGeometry],
  );

  const isVisible = visible || dragging;

  return {
    setScrollContainer,
    contentRef,
    trackRef,
    hasScroll,
    isVisible,
    isHidePending,
    handleTop,
    handleHeight,
    onScroll: handleScroll,
    onMouseEnter: handleMouseEnter,
    onMouseLeave: handleMouseLeave,
    onTrackClick: handleTrackClick,
    onHandleMouseDown: handleMouseDown,
  };
}
