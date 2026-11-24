import { useCallback, useEffect, useRef, useState, type CSSProperties, type HTMLAttributes, type MouseEvent, type PointerEvent, type RefCallback, type RefObject } from 'react';
import { FITTED, movedBy, toggledAt, zoomedAt, type ImageView, type Point, type Size } from '../lib/preview/imageZoom';
import { cn } from '../ui';

/** What an element fitted to a frame, and the frame, are drawn with for it to zoom and move. */
export interface Zoom<Zoomed extends HTMLElement> {
  /** Spread on the frame, a `div` that centres what it holds in all the space there is; it may come and go. */
  frame: HTMLAttributes<HTMLDivElement> & { ref: RefCallback<HTMLDivElement> };
  /** For the element in it that zooms, whose layout the zoom leaves alone; while it is not laid out, nothing zooms. */
  zoomed: { ref: RefObject<Zoomed | null>; style: CSSProperties };
}

/** Zooms an element with the wheel, a pinch or a double-click, and moves it with a drag. */
export function useZoom<Zoomed extends HTMLElement>(): Zoom<Zoomed> {
  const frame = useRef<HTMLDivElement>(null);
  /** The frame as it is drawn, whose wheel is heard. */
  const [framed, setFramed] = useState<HTMLDivElement | null>(null);
  const toFrame = useCallback((element: HTMLDivElement | null) => {
    frame.current = element;
    setFramed(element);
  }, []);
  const zoomed = useRef<Zoomed>(null);
  const [view, setView] = useState(FITTED);
  /** The pointers down on the frame, where each was last: one drags, two pinch. */
  const pointers = useRef(new Map<number, Point>());

  /** Changes the view, given the sizes it is kept within. */
  const change = useCallback((next: (view: ImageView, fitted: Size, frame: Size) => ImageView) => {
    if (!frame.current || !zoomed.current) return;
    // The layout's sizes, which the zoom's transform leaves alone.
    const fitted = { width: zoomed.current.offsetWidth, height: zoomed.current.offsetHeight };
    // Not shown yet, it would be shown zoomed by what nobody saw.
    if (fitted.width === 0 || fitted.height === 0) return;
    const within = { width: frame.current.clientWidth, height: frame.current.clientHeight };
    setView((current) => next(current, fitted, within));
  }, []);

  // Heard outside React, whose wheel listeners cannot keep the page from scrolling or zooming itself.
  useEffect(() => {
    const element = framed;
    if (!element) return;
    function onWheel(event: WheelEvent): void {
      event.preventDefault();
      const point = pointIn(element!, event);
      const factor = wheelFactor(event);
      change((current, fitted, within) => zoomedAt(current, factor, point, fitted, within));
    }
    element.addEventListener('wheel', onWheel, { passive: false });
    return () => element.removeEventListener('wheel', onWheel);
  }, [framed, change]);

  function onPointerDown(event: PointerEvent<HTMLDivElement>): void {
    if (event.button !== 0) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    pointers.current.set(event.pointerId, pointIn(event.currentTarget, event));
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>): void {
    const before = pointers.current.get(event.pointerId);
    if (!before) return;
    const now = pointIn(event.currentTarget, event);
    const other = [...pointers.current].find(([id]) => id !== event.pointerId)?.[1];
    pointers.current.set(event.pointerId, now);
    const by = (from: Point, to: Point) => ({ x: to.x - from.x, y: to.y - from.y });
    if (!other) {
      change((current, fitted, within) => movedBy(current, by(before, now), fitted, within));
      return;
    }
    const middleBefore = middle(before, other);
    const middleNow = middle(now, other);
    const factor = distance(now, other) / Math.max(1, distance(before, other));
    change((current, fitted, within) => zoomedAt(movedBy(current, by(middleBefore, middleNow), fitted, within), factor, middleNow, fitted, within));
  }

  function onPointerEnd(event: PointerEvent<HTMLDivElement>): void {
    pointers.current.delete(event.pointerId);
  }

  function onDoubleClick(event: MouseEvent<HTMLDivElement>): void {
    const point = pointIn(event.currentTarget, event);
    change((current, fitted, within) => toggledAt(current, point, fitted, within));
  }

  return {
    frame: {
      ref: toFrame,
      className: cn('flex min-h-0 flex-1 touch-none select-none items-center justify-center overflow-hidden', view.scale > 1 ? 'cursor-grab active:cursor-grabbing' : 'cursor-zoom-in'),
      onPointerDown,
      onPointerMove,
      onPointerUp: onPointerEnd,
      onPointerCancel: onPointerEnd,
      onDoubleClick,
    },
    zoomed: { ref: zoomed, style: { transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})` } },
  };
}

/** Where a pointer is from the middle of `element`. */
function pointIn(element: Element, { clientX, clientY }: { clientX: number; clientY: number }): Point {
  const { left, top, width, height } = element.getBoundingClientRect();
  return { x: clientX - left - width / 2, y: clientY - top - height / 2 };
}

/** How much a turn of the wheel zooms: a trackpad's pinch comes as a turn with Ctrl held, in smaller steps. */
function wheelFactor(event: WheelEvent): number {
  const pixels = event.deltaMode === WheelEvent.DOM_DELTA_LINE ? event.deltaY * 16 : event.deltaY;
  return Math.exp(-pixels * (event.ctrlKey ? 0.01 : 0.002));
}

function middle(one: Point, other: Point): Point {
  return { x: (one.x + other.x) / 2, y: (one.y + other.y) / 2 };
}

function distance(one: Point, other: Point): number {
  return Math.hypot(one.x - other.x, one.y - other.y);
}
