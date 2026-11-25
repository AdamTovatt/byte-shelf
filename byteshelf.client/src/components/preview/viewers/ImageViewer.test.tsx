// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ImageViewer } from './ImageViewer';
import { blobReader, type ContentReader } from '../../../lib/content/contentReader';
import { press, untilFound } from '../../../test/dom';
import { viewerProps } from '../../../test/viewerProps';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the image viewer', () => {
  let root: Root;
  let container: HTMLDivElement;

  async function show(type: string, content: ContentReader = blobReader(new Blob(['<svg/>'], { type }))): Promise<HTMLImageElement> {
    await act(async () => root.render(<ImageViewer {...viewerProps({ name: 'a.svg' }, content)} />));
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
    await act(async () => root.render(<ImageViewer key="second" {...viewerProps({ name: 'a' }, content)} />));
    await vi.waitFor(() => expect(container.textContent).toBe('The server is unreachable.Try again'));
  });

  it('zooms its image, as useZoom does', async () => {
    const image = await show('image/png');
    Object.defineProperties(image, { offsetWidth: { value: 400 }, offsetHeight: { value: 200 } });
    const frame = image.parentElement!;
    Object.defineProperties(frame, { clientWidth: { value: 400 }, clientHeight: { value: 300 } });
    frame.getBoundingClientRect = () => ({ left: 0, top: 0, width: 400, height: 300 }) as DOMRect;
    frame.setPointerCapture = () => {};
    image.getBoundingClientRect = () => ({ left: 0, right: 400, top: 50, bottom: 250 }) as DOMRect;

    // A click in its middle.
    act(() => {
      frame.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerId: 1, clientX: 200, clientY: 150, button: 0, pointerType: 'mouse' }));
      frame.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 200, clientY: 150 }));
    });

    expect(image.style.transform).toBe('translate(0px, 0px) scale(2.5)');
  });
});
