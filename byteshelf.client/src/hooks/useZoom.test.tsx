// @vitest-environment happy-dom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useZoom } from './useZoom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** A frame and what zooms in it; a `hidden` frame is drawn once Show is pressed. */
function Zoomable({ hidden = false }: { hidden?: boolean }) {
  const { frame, zoomed } = useZoom<HTMLDivElement>();
  const [isShown, setIsShown] = useState(!hidden);
  return isShown
    ? <div {...frame} data-frame><div {...zoomed} data-zoomed /></div>
    : <button onClick={() => setIsShown(true)}>Show</button>;
}

describe('zooming', () => {
  let root: Root;
  let container: HTMLDivElement;
  let zoomed: HTMLElement;
  let frame: HTMLElement;

  /** Shows it, in a 400 by 300 frame, fitted to the frame's width at `width` by 200, which the test page does not lay out itself. */
  async function show(width = 400): Promise<void> {
    await act(async () => root.render(<Zoomable />));
    lay(width);
  }

  function lay(width: number): void {
    zoomed = container.querySelector('[data-zoomed]')!;
    frame = container.querySelector('[data-frame]')!;
    const size = (element: HTMLElement, names: [string, string], width: number, height: number) => {
      Object.defineProperty(element, names[0], { value: width });
      Object.defineProperty(element, names[1], { value: height });
    };
    size(zoomed, ['offsetWidth', 'offsetHeight'], width, width === 0 ? 0 : 200);
    size(frame, ['clientWidth', 'clientHeight'], 400, 300);
    frame.getBoundingClientRect = () => ({ left: 0, top: 0, width: 400, height: 300 }) as DOMRect;
    frame.setPointerCapture = () => {};
  }

  beforeEach(() => {
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  function view(): { scale: number; x: number; y: number } {
    const [, x, y, scale] = zoomed.style.transform.match(/translate\((.+)px, (.+)px\) scale\((.+)\)/)!;
    return { scale: Number(scale), x: Number(x), y: Number(y) };
  }

  /** A turn of the wheel: a mouse event, whose position and modifiers the test page's WheelEvent leaves out. */
  function wheel({ deltaY, deltaMode, ...init }: WheelEventInit): MouseEvent {
    const event = new MouseEvent('wheel', { bubbles: true, cancelable: true, clientX: 200, clientY: 150, ...init });
    Object.assign(event, { deltaY, deltaMode });
    act(() => void frame.dispatchEvent(event));
    return event;
  }

  function pointer(type: string, pointerId: number, clientX: number, clientY: number, button = 0): void {
    act(() => void frame.dispatchEvent(new PointerEvent(type, { bubbles: true, pointerId, clientX, clientY, button })));
  }

  function doubleClick(clientX: number, clientY: number): void {
    act(() => void frame.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX, clientY })));
  }

  it('follows the wheel, about the pointer, which no longer scrolls the page', async () => {
    await show();
    expect(wheel({ deltaY: -100, clientX: 300 }).defaultPrevented).toBe(true);
    const zoomedIn = view();
    expect(zoomedIn.scale).toBeCloseTo(Math.exp(0.2));
    expect(zoomedIn.x).toBeCloseTo(100 * (1 - Math.exp(0.2)));

    wheel({ deltaY: 1000 });
    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('takes a trackpad\'s pinch in larger steps, and a wheel that counts lines as many pixels', async () => {
    await show();
    wheel({ deltaY: -100, ctrlKey: true });
    expect(view().scale).toBeCloseTo(Math.exp(1));

    wheel({ deltaY: 1000 });
    wheel({ deltaY: -10, deltaMode: WheelEvent.DOM_DELTA_LINE });
    expect(view().scale).toBeCloseTo(Math.exp(0.32));
  });

  it('hears the wheel of a frame that is drawn later', async () => {
    await act(async () => root.render(<Zoomable hidden />));
    await act(async () => container.querySelector('button')!.click());
    lay(400);

    wheel({ deltaY: -100, ctrlKey: true });

    expect(view().scale).toBeCloseTo(Math.exp(1));
  });

  it('zooms nothing that is not laid out yet', async () => {
    await show(0);

    doubleClick(200, 150);
    wheel({ deltaY: -100 });

    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('zooms in on a double-click and out on the next', async () => {
    await show();
    doubleClick(200, 150);
    expect(view().scale).toBe(2.5);

    doubleClick(200, 150);
    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('moves a zoomed image with a drag of the main button, until it is let go', async () => {
    await show();
    doubleClick(200, 150);

    pointer('pointerdown', 1, 100, 100, 2);
    pointer('pointermove', 1, 150, 110);
    expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
    pointer('pointerdown', 1, 100, 100);
    pointer('pointermove', 1, 150, 110);
    expect(view()).toEqual({ scale: 2.5, x: 50, y: 10 });
    pointer('pointerup', 1, 150, 110);
    pointer('pointermove', 1, 300, 110);
    expect(view()).toEqual({ scale: 2.5, x: 50, y: 10 });
  });

  it('zooms with a pinch of two fingers, about their middle, and moves with it', async () => {
    await show();
    doubleClick(200, 150);
    pointer('pointerdown', 1, 150, 150);
    pointer('pointerdown', 2, 250, 150);

    pointer('pointermove', 2, 270, 150);

    expect(view()).toEqual({ scale: 3, x: 10, y: 0 });
  });

  it('takes a finger the browser took over as lifted', async () => {
    await show();
    doubleClick(200, 150);
    pointer('pointerdown', 1, 100, 100);
    pointer('pointercancel', 1, 100, 100);

    pointer('pointermove', 1, 150, 100);

    expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
  });
});
