// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ImageViewer } from './ImageViewer';
import { blobReader, type ContentReader } from '../../../lib/content/contentReader';
import type { ShelfFile } from '../../../services/shelfService';
import { press, untilFound } from '../../../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the image viewer', () => {
  let root: Root;
  let container: HTMLDivElement;

  async function show(type: string, content: ContentReader = blobReader(new Blob(['<svg/>'], { type }))): Promise<HTMLImageElement> {
    await act(async () => root.render(<ImageViewer file={{ name: 'a.svg' } as ShelfFile} content={content} />));
    return untilFound<HTMLImageElement>('img');
  }

  beforeEach(() => {
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
  });

  it('shows a blob-backed SVG in an img from a data: URL, which a tab cannot run as the app', async () => {
    const image = await show('image/svg+xml');

    expect(image.alt).toBe('a.svg');
    expect(image.src).toMatch(/^data:image\/svg\+xml/);
  });

  it('says the browser cannot show an image it cannot decode, and shows one whose connection was lost when Try again is pressed', async () => {
    const image = await show('image/heic');

    await act(async () => void image.dispatchEvent(new Event('error')));
    await vi.waitFor(() => expect(container.textContent).toBe('This browser cannot show this image.Try again'));
    await press('Try again');

    await untilFound('img');
  });

  it('says why an image could not be read, whether its URL or its contents fail', async () => {
    const unreachable = async () => {
      throw new Error('The server is unreachable.');
    };
    const image = await show('image/png', { ...blobReader(new Blob(['a'])), range: unreachable });
    await act(async () => void image.dispatchEvent(new Event('error')));
    await vi.waitFor(() => expect(container.textContent).toBe('The server is unreachable.Try again'));

    const content = { ...blobReader(new Blob(['a'])), url: unreachable };
    await act(async () => root.render(<ImageViewer key="second" file={{ name: 'a' } as ShelfFile} content={content} />));
    await vi.waitFor(() => expect(container.textContent).toBe('The server is unreachable.Try again'));
  });

  describe('zooming', () => {
    let image: HTMLImageElement;
    let frame: HTMLElement;

    // A 400 by 300 frame, and an image fitted to its width, which the test page does not lay out itself.
    beforeEach(async () => {
      image = await show('image/png');
      frame = image.parentElement!;
      const size = (element: HTMLElement, names: [string, string], width: number, height: number) => {
        Object.defineProperty(element, names[0], { value: width });
        Object.defineProperty(element, names[1], { value: height });
      };
      size(image, ['offsetWidth', 'offsetHeight'], 400, 200);
      size(frame, ['clientWidth', 'clientHeight'], 400, 300);
      frame.getBoundingClientRect = () => ({ left: 0, top: 0, width: 400, height: 300 }) as DOMRect;
      frame.setPointerCapture = () => {};
    });

    function view(): { scale: number; x: number; y: number } {
      const [, x, y, scale] = image.style.transform.match(/translate\((.+)px, (.+)px\) scale\((.+)\)/)!;
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

    it('follows the wheel, about the pointer, which no longer scrolls the page', () => {
      expect(wheel({ deltaY: -100, clientX: 300 }).defaultPrevented).toBe(true);
      const zoomed = view();
      expect(zoomed.scale).toBeCloseTo(Math.exp(0.2));
      expect(zoomed.x).toBeCloseTo(100 * (1 - Math.exp(0.2)));

      wheel({ deltaY: 1000 });
      expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
    });

    it('takes a trackpad\'s pinch in larger steps, and a wheel that counts lines as many pixels', () => {
      wheel({ deltaY: -100, ctrlKey: true });
      expect(view().scale).toBeCloseTo(Math.exp(1));

      wheel({ deltaY: 1000 });
      wheel({ deltaY: -10, deltaMode: WheelEvent.DOM_DELTA_LINE });
      expect(view().scale).toBeCloseTo(Math.exp(0.32));
    });

    it('zooms in on a double-click and out on the next', () => {
      doubleClick(200, 150);
      expect(view().scale).toBe(2.5);

      doubleClick(200, 150);
      expect(view()).toEqual({ scale: 1, x: 0, y: 0 });
    });

    it('moves a zoomed image with a drag of the main button, until it is let go', () => {
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

    it('zooms with a pinch of two fingers, about their middle, and moves with it', () => {
      doubleClick(200, 150);
      pointer('pointerdown', 1, 150, 150);
      pointer('pointerdown', 2, 250, 150);

      pointer('pointermove', 2, 270, 150);

      expect(view()).toEqual({ scale: 3, x: 10, y: 0 });
    });

    it('takes a finger the browser took over as lifted', () => {
      doubleClick(200, 150);
      pointer('pointerdown', 1, 100, 100);
      pointer('pointercancel', 1, 100, 100);

      pointer('pointermove', 1, 150, 100);

      expect(view()).toEqual({ scale: 2.5, x: 0, y: 0 });
    });
  });
});
