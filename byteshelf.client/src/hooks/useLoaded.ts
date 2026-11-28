import { useCallback, useEffect, useRef, useState } from 'react';

/** Where a load is. */
export type Loaded<T> = { status: 'loading' } | { status: 'done'; value: T } | { status: 'failed'; error: unknown };

/** A load that can be run again. */
export type Reloadable<T> = Loaded<T> & { reload: () => void };

const LOADING = { status: 'loading' } as const;

/**
 * What `load` answers for `key`: run once for each key (compared by identity), aborted when the key
 * changes or the component is gone. A null key loads nothing, and stays loading, as for what is not to be
 * loaded yet. `reload` runs it again. For what must
 * not outlive the component in a cache, such as a file's decrypted contents; anything else is a query.
 */
export function useLoaded<T>(key: unknown, load: (signal: AbortSignal) => Promise<T>): Reloadable<T> {
  const [settled, setSettled] = useState<{ key: unknown; result: Loaded<T> } | null>(null);
  const [run, setRun] = useState(0);
  const latest = useRef(load);
  useEffect(() => {
    latest.current = load;
  });

  useEffect(() => {
    if (key === null) return;
    const loading = new AbortController();
    function settle(result: Loaded<T>): void {
      if (!loading.signal.aborted) setSettled({ key, result });
    }
    latest.current(loading.signal).then((value) => settle({ status: 'done', value }), (error: unknown) => settle({ status: 'failed', error }));
    return () => loading.abort();
  }, [key, run]);

  const reload = useCallback(() => {
    setSettled(null);
    setRun((before) => before + 1);
  }, []);

  return { ...(settled !== null && settled.key === key ? settled.result : LOADING), reload };
}
