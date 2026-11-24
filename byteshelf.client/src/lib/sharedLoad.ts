/** Joins the load of `key`: starts `load` when none of it is under way or kept, and answers what it does. */
export type JoinLoad<T> = (key: unknown, load: (signal: AbortSignal) => Promise<T | null>, signal?: AbortSignal) => Promise<T | null>;

/**
 * One load at a time, shared by all who want what it answers for its key and kept once answered. It is
 * aborted, or what it answered let go, once no one who joined wants it: until their signal aborts, or
 * without one until it ends. A load that failed or answered null is not kept, so the next to join loads
 * again, and another key's takes its place.
 */
export function sharedLoad<T>(): JoinLoad<T> {
  interface Load {
    key: unknown;
    result: Promise<T | null>;
    loading: AbortController;
    wanted: number;
  }
  let kept: Load | null = null;

  function start(key: unknown, load: (signal: AbortSignal) => Promise<T | null>): Load {
    const loading = new AbortController();
    const started: Load = { key, result: load(loading.signal), loading, wanted: 0 };
    started.result.then((value) => value === null && forget(started), () => forget(started));
    return started;
  }

  function forget(load: Load): void {
    if (kept === load) kept = null;
  }

  function leave(load: Load): void {
    if (--load.wanted > 0) return;
    load.loading.abort();
    forget(load);
  }

  return (key, load, signal) => {
    if (kept === null || kept.key !== key) kept = start(key, load);
    const joined = kept;
    joined.wanted++;
    if (signal) signal.addEventListener('abort', () => leave(joined), { once: true });
    else joined.result.then(() => leave(joined), () => leave(joined));
    return joined.result;
  };
}
