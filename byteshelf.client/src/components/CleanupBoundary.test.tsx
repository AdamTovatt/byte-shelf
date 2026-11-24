// @vitest-environment happy-dom
import { act, useEffect, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CleanupBoundary } from './CleanupBoundary';
import { ErrorBoundary } from './ErrorBoundary';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function ThrowsAsItGoes() {
  useEffect(() => () => { throw new Error('It failed as it went.'); }, []);
  return <p>first</p>;
}

function ThrowsAsItIsDrawn(): ReactNode {
  throw new Error('It failed as it was drawn.');
}

function ThrowsOnceItShows() {
  useEffect(() => { throw new Error('It failed once it showed.'); });
  return <p>third</p>;
}

describe('a boundary for what throws as it goes', () => {
  let root: Root;
  let container: HTMLDivElement;

  beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    container = document.body.appendChild(document.createElement('div'));
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('shows what takes its place', async () => {
    await act(async () => root.render(<CleanupBoundary><ThrowsAsItGoes /></CleanupBoundary>));
    await act(async () => root.render(<CleanupBoundary><p>second</p></CleanupBoundary>));

    expect(container.textContent).toBe('second');
  });

  it('draws what is left once, though it too throws as it goes', async () => {
    let drawn = 0;
    function Counted() {
      useEffect(() => {
        drawn++;
        return () => {
          // Not for ever, which would be tried for as long.
          if (drawn < 50) throw new Error('It failed as it went.');
        };
      }, []);
      return <p>other</p>;
    }
    await act(async () => root.render(<CleanupBoundary><Counted key="1" /><Counted key="2" /><Counted key="3" /></CleanupBoundary>));
    await act(async () => root.render(<CleanupBoundary><Counted key="2" /><Counted key="3" /></CleanupBoundary>));

    expect(container.textContent).toBe('otherother');
    expect(drawn).toBe(5);
    drawn = 50;
  });

  it.each([
    ['is drawn', 'at first', null, <ThrowsAsItIsDrawn />],
    ['is drawn', 'after that', <p>first</p>, <ThrowsAsItIsDrawn />],
    ['shows', 'at first', null, <ThrowsOnceItShows />],
    ['shows', 'after that', <p>first</p>, <ThrowsOnceItShows />],
  ])('leaves to the boundary around it what throws each time it %s, %s', async (_, __, before, child) => {
    const draw = (children: ReactNode) => act(async () => root.render(<ErrorBoundary fallback="around it"><CleanupBoundary>{children}</CleanupBoundary></ErrorBoundary>));
    if (before) await draw(before);
    await draw(child);

    expect(container.textContent).toBe('around it');
  });
});
