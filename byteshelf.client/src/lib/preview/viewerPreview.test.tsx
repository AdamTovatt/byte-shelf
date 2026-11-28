// @vitest-environment happy-dom
import { act, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { InPreview, stubFullScreen, type FullScreenStub } from '../../plugin/testing';
import { usePreviewFullScreen, usePreviewKeys } from './viewerPreview';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('a viewer', () => {
  let root: Root;
  let fullScreen: FullScreenStub;
  const heard: string[] = [];

  /** Hears keys, and goes full screen on a click. */
  function Viewer() {
    usePreviewKeys((event) => void heard.push(event.key));
    const { canFullScreen, isFullScreen, toggle } = usePreviewFullScreen();
    return <button type="button" onClick={toggle}>{`${canFullScreen} ${isFullScreen}`}</button>;
  }

  async function show(viewer: ReactNode): Promise<HTMLButtonElement> {
    await act(async () => root.render(viewer));
    return document.querySelector('button')!;
  }

  beforeEach(() => {
    heard.length = 0;
    fullScreen = stubFullScreen();
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    fullScreen.restore();
  });

  it('outside a preview hears no keys and cannot go full screen', async () => {
    const shown = await show(<Viewer />);

    await act(async () => void document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k' })));
    await act(async () => shown.click());

    expect([heard, fullScreen.requested, shown.textContent]).toEqual([[], [], 'false false']);
  });

  it('in a preview is full screen while the page is, and not while another element is', async () => {
    const shown = await show(<InPreview><Viewer /></InPreview>);
    const texts = [shown.textContent];

    await act(async () => fullScreen.show(shown));
    texts.push(shown.textContent);
    await act(async () => fullScreen.show(document.documentElement));
    texts.push(shown.textContent);

    expect(texts).toEqual(['true false', 'true false', 'true true']);
  });
});
