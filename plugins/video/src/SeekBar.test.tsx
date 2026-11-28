import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SeekBar } from './SeekBar';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const STORYBOARD = { url: 'preview.webp', layout: { intervalSeconds: 10, columns: 5, rows: 2, frameWidth: 160, frameHeight: 90, count: 10 } };

describe('the seek bar', () => {
  let root: Root;
  let container: HTMLDivElement;
  let onSeek: ReturnType<typeof vi.fn<(seconds: number) => void>>;

  /** The bar of a video of 100 seconds at 50, 200 pixels wide from the left of the page. */
  async function show(): Promise<HTMLElement> {
    await act(async () => root.render(<SeekBar durationSeconds={100} currentSeconds={50} buffered={[]} onSeek={onSeek} storyboard={STORYBOARD} />));
    const bar = container.querySelector<HTMLElement>('[role="slider"]')!;
    bar.getBoundingClientRect = () => ({ left: 0, width: 200 }) as DOMRect;
    bar.setPointerCapture = () => {};
    return bar;
  }

  async function point(bar: HTMLElement, type: string, clientX: number, pointerType = 'mouse'): Promise<void> {
    await act(async () => void bar.dispatchEvent(new PointerEvent(type, { clientX, pointerType, button: 0, bubbles: true })));
  }

  function frame(): string | null {
    return container.querySelector('[data-frame]')?.getAttribute('data-frame') ?? null;
  }

  beforeEach(() => {
    onSeek = vi.fn();
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
  });

  it('shows the storyboard\'s frame of where the mouse is over it, and none once it leaves', async () => {
    const bar = await show();

    await point(bar, 'pointermove', 130);
    // 65 seconds: frame 6, at column 1 of row 1.
    expect([frame(), container.textContent]).toEqual(['160,90', '1:05']);

    // React's leave is the pointer going out to what is outside the bar.
    await act(async () => void bar.dispatchEvent(new PointerEvent('pointerout', { pointerType: 'mouse', relatedTarget: document.body, bubbles: true })));
    expect(frame()).toBeNull();
  });

  it('is dragged to where it is let go, showing where it goes as it goes, and seeks nowhere when the drag is called off', async () => {
    const bar = await show();

    await point(bar, 'pointerdown', 20, 'touch');
    await point(bar, 'pointermove', 60, 'touch');
    expect([bar.getAttribute('aria-valuenow'), frame()]).toEqual(['30', '480,0']);
    await point(bar, 'pointerup', 80, 'touch');
    expect([onSeek.mock.calls, frame()]).toEqual([[[40]], null]);

    await point(bar, 'pointerdown', 20, 'touch');
    await point(bar, 'pointercancel', 20, 'touch');
    expect([onSeek.mock.calls.length, bar.getAttribute('aria-valuenow'), frame()]).toEqual([1, '50', null]);
  });

  it('goes to the start with Home and the end with End, and 5 seconds back and on with the arrows', async () => {
    const bar = await show();

    for (const key of ['Home', 'End', 'ArrowLeft', 'ArrowRight']) {
      await act(async () => void bar.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })));
    }

    expect(onSeek.mock.calls).toEqual([[0], [100], [45], [55]]);
  });
});
