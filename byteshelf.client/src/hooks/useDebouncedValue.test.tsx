// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useDebouncedValue } from './useDebouncedValue';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('useDebouncedValue', () => {
  let root: Root;
  let settled: string;

  function Probe({ value }: { value: string }) {
    settled = useDebouncedValue(value, 250);
    return null;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    root = createRoot(document.createElement('div'));
    act(() => root.render(<Probe value="a" />));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.useRealTimers();
  });

  it('answers a new value only once it has stopped changing for the delay', () => {
    act(() => root.render(<Probe value="ab" />));
    act(() => vi.advanceTimersByTime(200));
    act(() => root.render(<Probe value="abc" />));
    act(() => vi.advanceTimersByTime(200));

    expect(settled).toBe('a');

    act(() => vi.advanceTimersByTime(50));

    expect(settled).toBe('abc');
  });
});
