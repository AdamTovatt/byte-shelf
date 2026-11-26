import { act } from 'react';

// Pointers as a mouse or a finger moves them, on a test page that has neither and lays nothing out.

/** Where a pointer is: its `clientX` and `clientY`. */
export type At = [number, number];

/** A pointer event of `type` on `target`. */
export function pointer(type: string, target: Element, init: PointerEventInit = {}): void {
  act(() => void target.dispatchEvent(new PointerEvent(type, { bubbles: true, ...init })));
}

/**
 * A pointer put down on `target` at the first point, moved through the rest and lifted at the last, all before a
 * render: the main button of a finger unless `init` says otherwise.
 */
export function drag(target: Element, points: At[], init: PointerEventInit = {}): void {
  const at = ([clientX, clientY]: At) => ({ bubbles: true, pointerId: 1, button: 0, pointerType: 'touch', ...init, clientX, clientY });
  act(() => {
    target.dispatchEvent(new PointerEvent('pointerdown', at(points[0])));
    for (const point of points.slice(1)) target.dispatchEvent(new PointerEvent('pointermove', at(point)));
    target.dispatchEvent(new PointerEvent('pointerup', at(points[points.length - 1])));
  });
}

/** Two taps of a finger on `target` at `at`, which the browser follows with a double-click. */
export function doubleTap(target: Element, [clientX, clientY]: At): void {
  const touch = { bubbles: true, pointerId: 9, clientX, clientY, button: 0, pointerType: 'touch' };
  act(() => {
    target.dispatchEvent(new PointerEvent('pointerdown', touch));
    target.dispatchEvent(new PointerEvent('pointerup', touch));
    target.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX, clientY }));
  });
}

/**
 * Lays out a `useZoom` frame 400 by 300 at the screen's top left corner, and what zooms in it, its first element,
 * `zoomed` large, and has the frame hold a pointer pressed on it.
 */
export function layOutZoom(frame: HTMLElement, zoomed: { width: number; height: number }): void {
  Object.defineProperties(frame.firstElementChild, { offsetWidth: { value: zoomed.width }, offsetHeight: { value: zoomed.height } });
  Object.defineProperties(frame, { clientWidth: { value: 400 }, clientHeight: { value: 300 } });
  frame.getBoundingClientRect = () => ({ left: 0, top: 0, width: 400, height: 300 }) as DOMRect;
  frame.setPointerCapture = () => {};
}
