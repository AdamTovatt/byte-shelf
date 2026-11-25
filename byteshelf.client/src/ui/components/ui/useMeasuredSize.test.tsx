// @vitest-environment happy-dom
import { act, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { useMeasuredSize } from './useMeasuredSize';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let root: Root;
let container: HTMLDivElement;

/** An element drawn until Hide is pressed, and the height measured of it. */
function Measured() {
  const { ref, height } = useMeasuredSize<HTMLDivElement>();
  const [isDrawn, setIsDrawn] = useState(true);
  return <>{isDrawn && <div ref={ref} />}<output>{height}</output><button onClick={() => setIsDrawn(false)}>Hide</button></>;
}

beforeEach(() => {
  // The test page lays nothing out.
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 100, height: 40 } as DOMRect);
  container = document.body.appendChild(document.createElement('div'));
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.restoreAllMocks();
});

it('measures the element while it is drawn, and nothing once it is not', async () => {
  await act(async () => root.render(<Measured />));
  expect(container.querySelector('output')!.textContent).toBe('40');

  await act(async () => container.querySelector('button')!.click());
  expect(container.querySelector('output')!.textContent).toBe('0');
});
