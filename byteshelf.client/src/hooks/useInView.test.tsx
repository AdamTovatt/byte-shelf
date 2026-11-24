// @vitest-environment happy-dom
import { act, useRef } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useInView } from './useInView';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** One that watches, and what it was made with. */
interface Watching {
  see: (isIntersecting: boolean) => void;
  options: IntersectionObserverInit;
  observed: Element[];
  isDisconnected: boolean;
}

describe('whether an element is in view', () => {
  let root: Root;
  let watching: Watching[];

  function Probe() {
    const element = useRef<HTMLSpanElement>(null);
    return <span id="probe" ref={element}>{useInView(element) ? 'in view' : 'not yet'}</span>;
  }

  beforeEach(() => {
    watching = [];
    vi.stubGlobal('IntersectionObserver', class {
      private readonly mine: Watching;
      constructor(seen: (entries: { isIntersecting: boolean }[]) => void, options: IntersectionObserverInit) {
        this.mine = { see: (isIntersecting) => seen([{ isIntersecting }]), options, observed: [], isDisconnected: false };
        watching.push(this.mine);
      }
      observe(element: Element) {
        this.mine.observed.push(element);
      }
      disconnect() {
        this.mine.isDisconnected = true;
      }
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('is not known until it is seen, and stays so once it was', async () => {
    await act(async () => root.render(<Probe />));
    const probe = document.getElementById('probe')!;
    expect(watching.map((each) => each.observed)).toEqual([[probe]]);

    await act(async () => watching[0]!.see(false));
    expect(probe.textContent).toBe('not yet');

    await act(async () => watching[0]!.see(true));
    expect(probe.textContent).toBe('in view');
    expect(watching.map((each) => each.isDisconnected)).toEqual([true]);
  });

  it('is watched no longer once it is gone', async () => {
    await act(async () => root.render(<Probe />));
    await act(async () => root.render(null));

    expect(watching.map((each) => each.isDisconnected)).toEqual([true]);
  });

  it('is so 200 px outside of what scrolls it, which is the page when nothing else does', async () => {
    await act(async () => root.render(
      <main id="scroller" style={{ overflowY: 'auto' }}>
        <div style={{ overflowY: 'hidden' }}><Probe /></div>
      </main>,
    ));
    expect(watching[0]!.options).toEqual({ root: document.getElementById('scroller'), rootMargin: '200px' });

    await act(async () => root.render(<Probe />));
    expect(watching[1]!.options).toEqual({ root: null, rootMargin: '200px' });
  });
});
