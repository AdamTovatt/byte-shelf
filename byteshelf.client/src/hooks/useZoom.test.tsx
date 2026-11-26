// @vitest-environment happy-dom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useZoom } from './useZoom';
import { DRAGS_ITSELF } from '../lib/dragsItself';
import { BackdropClick } from '../lib/preview/backdropClick';
import { ZoomedSwipe } from '../lib/preview/swipe';
import { doubleTap, drag, layOutZoom, pointer, type At } from '../test/pointers';

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
    layOutZoom(frame, { width, height: width === 0 ? 0 : 200 });
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

  /** A pointer event of `type` on the frame, of the main button unless `init` says otherwise. */
  function onFrame(type: string, pointerId: number, [clientX, clientY]: At, init: PointerEventInit = {}): void {
    pointer(type, frame, { pointerId, clientX, clientY, button: 0, ...init });
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

    doubleTap(frame, [200, 150]);
    wheel({ deltaY: -100 });

    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('zooms in on a double tap and out on the next', async () => {
    await show();
    doubleTap(frame, [200, 150]);
    expect(view().scale).toBe(2.5);

    doubleTap(frame, [200, 150]);
    expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
  });

  it('marks the frame while zoomed in, when a drag moves what is shown', async () => {
    await show();
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(false);

    doubleTap(frame, [200, 150]);
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(true);
    doubleTap(frame, [200, 150]);
    expect(frame.hasAttribute(DRAGS_ITSELF)).toBe(false);
  });

  it('moves a zoomed image with a drag of the main button, until it is let go', async () => {
    await show();
    doubleTap(frame, [200, 150]);

    onFrame('pointerdown', 1, [100, 100], { button: 2 });
    onFrame('pointermove', 1, [150, 110]);
    expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
    onFrame('pointerdown', 1, [100, 100]);
    onFrame('pointermove', 1, [150, 110]);
    expect(view()).toEqual({ scale: 2.5, x: 50, y: 10 });
    onFrame('pointerup', 1, [150, 110]);
    onFrame('pointermove', 1, [300, 110]);
    expect(view()).toEqual({ scale: 2.5, x: 50, y: 10 });
  });

  it('zooms with a pinch of two fingers, about their middle, and moves with it', async () => {
    await show();
    doubleTap(frame, [200, 150]);
    onFrame('pointerdown', 1, [150, 150]);
    onFrame('pointerdown', 2, [250, 150]);

    onFrame('pointermove', 2, [270, 150]);

    expect(view()).toEqual({ scale: 3, x: 10, y: 0 });
  });

  it('takes a finger the browser took over as lifted', async () => {
    await show();
    doubleTap(frame, [200, 150]);
    onFrame('pointerdown', 1, [100, 100]);
    onFrame('pointercancel', 1, [100, 100]);

    onFrame('pointermove', 1, [150, 100]);

    expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
  });

  describe('a drag of a finger', () => {
    let swiped: string[];

    /** Shows it where a swipe is heard, drawn where `show` lays it out: zoomed in, 300 pixels from either side and 100 from the top or bottom. */
    async function showSwiping(): Promise<void> {
      swiped = [];
      const swipes = { onLeft: () => swiped.push('left'), onRight: () => swiped.push('right'), onDown: () => swiped.push('down') };
      await act(async () => root.render(<ZoomedSwipe.Provider value={swipes}><Zoomable /></ZoomedSwipe.Provider>));
      lay(400);
    }

    /** Zooms in and drags the image to its left edge, which a drag right goes past. */
    function toLeftEdge(): void {
      doubleTap(frame, [200, 150]);
      drag(frame, [[50, 150], [350, 150]]);
    }

    /** A finger's pointer event of `type` on the frame. */
    function finger(type: string, pointerId: number, at: At): void {
      onFrame(type, pointerId, at, { pointerType: 'touch' });
    }

    it('zoomed in, swipes once it goes as far as a swipe past where the image stops at its edge', async () => {
      await showSwiping();
      toLeftEdge();
      expect([view().x, swiped]).toEqual([300, []]);

      drag(frame, [[100, 150], [149, 150]]);
      expect(swiped).toEqual([]);
      drag(frame, [[100, 150], [150, 150]]);
      expect(swiped).toEqual(['right']);
      // 600 of it followed, to the other edge.
      drag(frame, [[380, 150], [-270, 150]]);
      expect([view().x, swiped]).toEqual([-300, ['right', 'left']]);
    });

    it('counts nothing of an edge it went past once the image follows it again', async () => {
      await showSwiping();
      toLeftEdge();

      // 150 past the edge, back and forth with the image, and let go 50 right of where it went down.
      drag(frame, [[100, 150], [250, 150], [50, 150], [150, 150]]);
      expect([view().x, swiped]).toEqual([200, []]);
    });

    it('swipes only as far as the finger went all told', async () => {
      await showSwiping();
      doubleTap(frame, [200, 150]);

      // Past the right edge, and back over to 60 past the left one, but only 30 right of where it went down.
      drag(frame, [[600, 150], [-30, 150], [570, 150], [630, 150]]);
      expect([view().x, swiped]).toEqual([300, []]);
    });

    it('closes at one down past the top, and not up past the bottom', async () => {
      await showSwiping();
      doubleTap(frame, [200, 150]);

      drag(frame, [[200, 50], [200, 150]]);
      expect([view().y, swiped]).toEqual([100, []]);
      drag(frame, [[200, 50], [200, 150]]);
      expect(swiped).toEqual(['down']);
      drag(frame, [[200, 250], [200, -150]]);
      expect([view().y, swiped]).toEqual([-100, ['down']]);
    });

    it('closes not at a pan sideways that drifts down over an image whose height fits', async () => {
      await showSwiping();
      // 1.49 times as large: 298 of the frame's 300 high, and 98 from either side.
      wheel({ deltaY: -100 });
      wheel({ deltaY: -100 });

      // Down 130 the image does not follow, but 160 across too.
      drag(frame, [[300, 100], [140, 230]]);
      expect(view().x).toBeCloseTo(-98.4, 1);
      expect(swiped).toEqual([]);
    });

    it('leaves a swipe of what is fitted to the preview, and the mouse\'s past an edge a drag', async () => {
      await showSwiping();
      drag(frame, [[300, 150], [100, 150]]);
      drag(frame, [[200, 50], [200, 250]]);
      expect(swiped).toEqual([]);

      doubleTap(frame, [200, 150]);
      drag(frame, [[50, 150], [350, 150]], { pointerType: 'mouse' });
      drag(frame, [[100, 150], [300, 150]], { pointerType: 'mouse' });
      expect([view().x, swiped]).toEqual([300, []]);
    });

    it('takes none that the browser took over', async () => {
      await showSwiping();
      toLeftEdge();

      finger('pointerdown', 1, [100, 150]);
      finger('pointermove', 1, [300, 150]);
      finger('pointercancel', 1, [300, 150]);
      expect(swiped).toEqual([]);
    });

    it('takes none that began as a pinch, whichever finger is left to go on past the edge', async () => {
      await showSwiping();
      toLeftEdge();

      finger('pointerdown', 1, [100, 150]);
      finger('pointerdown', 2, [300, 150]);
      finger('pointerup', 2, [300, 150]);
      finger('pointermove', 1, [300, 150]);
      finger('pointerup', 1, [300, 150]);

      finger('pointerdown', 1, [100, 150]);
      finger('pointerdown', 2, [300, 150]);
      finger('pointerup', 1, [100, 150]);
      finger('pointermove', 2, [500, 150]);
      finger('pointerup', 2, [500, 150]);
      expect([view().x, swiped]).toEqual([300, []]);
    });
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
