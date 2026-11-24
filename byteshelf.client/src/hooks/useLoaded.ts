import { useCallback, useEffect, useRef, useState } from 'react';

/** Where a load is. */
export type Loaded<T> = { status: 'loading' } | { status: 'done'; value: T } | { status: 'failed'; error: unknown };

const LOADING = { status: 'loading' } as const;

/**
 * What `load` answers for `key`: run once for each key (compared by identity), aborted when the key
 * changes or the component is gone, and not run while it is null. `reload` runs it again, as long
 * as the key it was answered for still is. For what must not outlive the component in a cache, such as a
 * file's decrypted contents; anything else is a query.
 */
export function useLoaded<T>(key: unknown, load: (signal: AbortSignal) => Promise<T>): Loaded<T> & { reload: () => void } {
  const [settled, setSettled] = useState<{ key: unknown; result: Loaded<T> } | null>(null);
  const [run, setRun] = useState(0);
  const latest = useRef({ key, load });
  useEffect(() => {
    latest.current = { key, load };
  });

  useEffect(() => {
    if (key === null) return;
    const loading = new AbortController();
    function settle(result: Loaded<T>): void {
      if (!loading.signal.aborted) setSettled({ key, result });
    }
    latest.current.load(loading.signal).then((value) => settle({ status: 'done', value }), (error: unknown) => settle({ status: 'failed', error }));
    return () => loading.abort();
  }, [key, run]);

  const reload = useCallback(() => {
    if (latest.current.key !== key) return;
    setSettled(null);
    setRun((before) => before + 1);
  }, [key]);

  return { ...(settled !== null && settled.key === key ? settled.result : LOADING), reload };
}
