import { useCallback, useEffect, useRef, useState, type MouseEvent, type PointerEvent } from 'react';
import { cn } from '../../../ui';
import { useLoaded } from '../../../hooks/useLoaded';
import { FITTED, movedBy, toggledAt, zoomedAt, type ImageView, type Point, type Size } from '../../../lib/preview/imageZoom';
import type { ViewerProps } from '../../../lib/preview/viewerRegistry';
import { LoadedContent } from '../LoadedContent';
import { PreviewMessage } from '../PreviewMessage';

/**
 * An image fitted to the preview: the wheel, a pinch or a double-click zooms, a drag moves it. Always an
 * `<img>`, where an SVG's scripts do not run.
 */
export function ImageViewer({ file, content }: ViewerProps) {
  const url = useLoaded(content, (signal) => content.url(signal));
  const [notShown, setNotShown] = useState(false);

  if (notShown) return <NotShown content={content} />;
  return <LoadedContent loaded={url}>{(src) => <ZoomableImage src={src} name={file.name} onError={() => setNotShown(true)} />}</LoadedContent>;
}

/** Why an image did not show: its contents could not be read, or else the browser cannot decode them. */
function NotShown({ content }: Pick<ViewerProps, 'content'>) {
  const read = useLoaded(content, (signal) => content.range(0, 1, signal));
  return <LoadedContent loaded={read}>{() => <PreviewMessage message="This browser cannot show this image." />}</LoadedContent>;
}

function ZoomableImage({ src, name, onError }: { src: string; name: string; onError: () => void }) {
  const frame = useRef<HTMLDivElement>(null);
  const image = useRef<HTMLImageElement>(null);
  const [view, setView] = useState(FITTED);
  /** The pointers down on the image, where each was last: one drags, two pinch. */
  const pointers = useRef(new Map<number, Point>());

  /** Changes the view, given the sizes it is kept within. */
  const change = useCallback((next: (view: ImageView, fitted: Size, frame: Size) => ImageView) => {
    if (!frame.current || !image.current) return;
    // The layout's sizes, which the zoom's transform leaves alone.
    const fitted = { width: image.current.offsetWidth, height: image.current.offsetHeight };
    const within = { width: frame.current.clientWidth, height: frame.current.clientHeight };
    setView((current) => next(current, fitted, within));
  }, []);

  // Heard outside React, whose wheel listeners cannot keep the page from scrolling or zooming itself.
  useEffect(() => {
    const element = frame.current;
    if (!element) return;
    function onWheel(event: WheelEvent): void {
      event.preventDefault();
      const point = pointIn(element!, event);
      const factor = wheelFactor(event);
      change((current, fitted, within) => zoomedAt(current, factor, point, fitted, within));
    }
    element.addEventListener('wheel', onWheel, { passive: false });
    return () => element.removeEventListener('wheel', onWheel);
  }, [change]);

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

  return (
    <div
      ref={frame}
      className={cn('flex min-h-0 flex-1 touch-none select-none items-center justify-center overflow-hidden', view.scale > 1 ? 'cursor-grab active:cursor-grabbing' : 'cursor-zoom-in')}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerEnd}
      onPointerCancel={onPointerEnd}
      onDoubleClick={onDoubleClick}
    >
      <img
        ref={image}
        src={src}
        alt={name}
        draggable={false}
        onError={onError}
        className="max-h-full max-w-full object-contain"
        style={{ transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})` }}
      />
    </div>
  );
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
