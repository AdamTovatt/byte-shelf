// @vitest-environment happy-dom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useZoom } from './useZoom';
import { DRAGS_ITSELF } from '../lib/dragsItself';
import { BackdropClick } from '../lib/preview/backdropClick';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** A frame and what zooms in it; a `hidden` frame is drawn once Show is pressed. */
function Zoomable({ hidden = false }: { hidden?: boolean }) {
  const { frame, zoomed } = useZoom<HTMLDivElement>();
  const [isShown, setIsShown] = useState(!hidden);
  return isShown
    ? <div {...frame} data-frame><div {...zoomed} data-content /></div>
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
    zoomed = container.querySelector('[data-content]')!;
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

  /** Two taps of a finger, which the browser follows with a double-click. */
  function doubleTap(clientX: number, clientY: number): void {
    const touch = { bubbles: true, pointerId: 9, clientX, clientY, button: 0, pointerType: 'touch' };
    act(() => {
      frame.dispatchEvent(new PointerEvent('pointerdown', touch));
      frame.dispatchEvent(new PointerEvent('pointerup', touch));
      frame.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX, clientY }));
    });
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

    doubleTap(200, 150);
    wheel({ deltaY: -100 });

    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('zooms in on a double tap and out on the next', async () => {
    await show();
    doubleTap(200, 150);
    expect(view().scale).toBe(2.5);

    doubleTap(200, 150);
    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('marks the frame while zoomed in, when a drag moves what is shown', async () => {
    await show();
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(false);

    doubleTap(200, 150);
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(true);
    doubleTap(200, 150);
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(false);
  });

  it('moves a zoomed image with a drag of the main button, until it is let go', async () => {
    await show();
    doubleTap(200, 150);

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
    doubleTap(200, 150);
    pointer('pointerdown', 1, 150, 150);
    pointer('pointerdown', 2, 250, 150);

    pointer('pointermove', 2, 270, 150);

    expect(view()).toEqual({ scale: 3, x: 10, y: 0 });
  });

  it('takes a finger the browser took over as lifted', async () => {
    await show();
    doubleTap(200, 150);
    pointer('pointerdown', 1, 100, 100);
    pointer('pointercancel', 1, 100, 100);

    pointer('pointermove', 1, 150, 100);

    expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
  });

  describe('a click', () => {
    let backdropClicks: number;

    /** Shows it under a backdrop that counts its clicks, drawn where `show` lays it out. */
    async function showOnBackdrop(): Promise<void> {
      backdropClicks = 0;
      await act(async () => root.render(<BackdropClick.Provider value={() => backdropClicks++}><Zoomable /></BackdropClick.Provider>));
      lay(400);
      zoomed.getBoundingClientRect = () => ({ left: 0, right: 400, top: 50, bottom: 250 }) as DOMRect;
    }

    /** A press at `from` let go at `to`, with `pointerType`. */
    function click(from: [number, number], to: [number, number] = from, pointerType = 'mouse'): void {
      act(() => {
        frame.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerId: 1, clientX: from[0], clientY: from[1], button: 0, pointerType }));
        frame.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, pointerId: 1, clientX: to[0], clientY: to[1], button: 0, pointerType }));
        frame.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: to[0], clientY: to[1] }));
      });
    }

    it('zooms in where it is on what zooms, and out at the next anywhere', async () => {
      await showOnBackdrop();

      click([300, 150]);
      expect(view()).toEqual({ scale: 2.5, x: -150, y: 0 });
      click([200, 20]);
      expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
      expect(backdropClicks).toBe(0);
    });

    it('is on the backdrop beside what zooms while it is fitted', async () => {
      await showOnBackdrop();

      click([200, 20]);
      click([200, 290]);
      expect(backdropClicks).toBe(2);
      expect(view().scale).toBe(1);
    });

    it('does nothing after a drag, or from a tap', async () => {
      await showOnBackdrop();

      click([200, 20], [210, 20]);
      click([200, 150], [200, 160]);
      click([200, 20], [200, 20], 'touch');
      click([200, 150], [200, 150], 'touch');
      expect([backdropClicks, view().scale]).toEqual([0, 1]);
    });

    it('of a mouse twice zooms in and out again, its double-click doing nothing more', async () => {
      await showOnBackdrop();

      click([200, 150]);
      click([200, 150]);
      act(() => void frame.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX: 200, clientY: 150 })));
      expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
    });
  });
});
