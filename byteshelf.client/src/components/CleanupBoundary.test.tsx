// @vitest-environment happy-dom
import { act, useEffect, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CleanupBoundary } from './CleanupBoundary';

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

  it.each([['is drawn', <ThrowsAsItIsDrawn />], ['shows', <ThrowsOnceItShows />]])('leaves to what is around it what throws each time it %s', async (_, child) => {
    const thrown = await act(async () => root.render(<CleanupBoundary>{child}</CleanupBoundary>)).then(() => null, (error: Error) => error.message ?? 'several');

    expect(thrown).not.toBeNull();
  });
});
