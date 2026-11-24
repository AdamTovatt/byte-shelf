// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useLoaded, type Loaded } from './useLoaded';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** A load under way: the key it is for, its signal, and how it ends. */
interface Load {
  key: string;
  signal: AbortSignal;
  resolve: (value: string) => void;
  reject: (error: Error) => void;
}

describe('a load', () => {
  let root: Root;
  let loads: Load[];
  let loaded: Loaded<string> & { reload: () => void };

  function Probe({ of }: { of: string | null }) {
    loaded = useLoaded(of, (signal) => new Promise<string>((resolve, reject) => loads.push({ key: of!, signal, resolve, reject })));
    return null;
  }

  function show(key: string | null): Promise<void> {
    return act(async () => root.render(<Probe of={key} />));
  }

  function state(): unknown {
    return loaded.status === 'done' ? loaded.value : loaded.status === 'failed' ? (loaded.error as Error).message : loaded.status;
  }

  beforeEach(() => {
    loads = [];
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => act(() => root.unmount()));

  it('runs once for a key, and answers its value or why it failed', async () => {
    await show('a');
    await show('a');
    expect([loads.length, state()]).toEqual([1, 'loading']);
    await act(async () => loads[0].resolve('A'));
    expect(state()).toBe('A');

    await show('b');
    expect(state()).toBe('loading');
    await act(async () => loads[1].reject(new Error('no b')));
    expect(state()).toBe('no b');
  });

  it('does not run while its key is null', async () => {
    await show(null);

    expect([loads.length, state()]).toEqual([0, 'loading']);
  });

  it('is aborted as its key changes, and what it then answers is left', async () => {
    await show('a');
    await show('b');
    expect(loads.map((load) => load.signal.aborted)).toEqual([true, false]);

    await act(async () => loads[1].resolve('B'));
    await act(async () => loads[0].resolve('A'));

    expect(state()).toBe('B');
  });

  it('is aborted as its component is gone', async () => {
    await show('a');

    await act(async () => root.render(null));

    expect(loads[0].signal.aborted).toBe(true);
  });

  it('is not run again by a reload answered for a key before', async () => {
    await show('a');
    const { reload } = loaded;
    await show('b');
    await act(async () => loads[1].resolve('B'));

    await act(async () => reload());

    expect([loads.length, state()]).toEqual([2, 'B']);
  });

  it('runs again when reloaded, loading until that ends', async () => {
    await show('a');
    await act(async () => loads[0].reject(new Error('no a')));

    await act(async () => loaded.reload());
    expect([loads.length, state()]).toEqual([2, 'loading']);
    await act(async () => loads[1].resolve('A'));

    expect(state()).toBe('A');
  });
});
