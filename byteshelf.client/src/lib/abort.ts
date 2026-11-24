/** A signal that aborts as `own` does, or as `signal`, when given, does. */
export function eitherAborts(own: AbortSignal, signal?: AbortSignal): AbortSignal {
  return signal ? AbortSignal.any([own, signal]) : own;
}

/** What `work` answers, waited for no longer once `signal` aborts: what it does goes on, for whoever else waits. */
export function untilAborted<T>(work: Promise<T>, signal?: AbortSignal): Promise<T> {
  if (!signal) return work;
  return new Promise((resolve, reject) => {
    const stop = () => reject(signal.reason as Error);
    if (signal.aborted) stop();
    signal.addEventListener('abort', stop, { once: true });
    void work.then(resolve, reject).finally(() => signal.removeEventListener('abort', stop));
  });
}

/** What fails for want of a password nobody gave. */
export function noPasswordGiven(): DOMException {
  return new DOMException('No password was given.', 'AbortError');
}

/** Whether something ended because it was given up, rather than failed. */
export function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
