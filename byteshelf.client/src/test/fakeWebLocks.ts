/**
 * Gives the test's `navigator` the Web Locks API, which happy-dom leaves out: exclusive locks by name,
 * granted in the order they were asked for. Answers how many requests each name has had.
 */
export function installFakeWebLocks(): Map<string, number> {
  const tails = new Map<string, Promise<unknown>>();
  const requests = new Map<string, number>();

  const locks = {
    request<T>(name: string, work: () => Promise<T>): Promise<T> {
      requests.set(name, (requests.get(name) ?? 0) + 1);
      const previous = tails.get(name) ?? Promise.resolve();
      const run = previous.then(work, work);
      tails.set(name, run.catch(() => undefined));
      return run;
    },
  };

  Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });
  return requests;
}

/** Takes the Web Locks API away again, as a browser does outside a secure context (plain HTTP). */
export function removeWebLocks(): void {
  Object.defineProperty(navigator, 'locks', { value: undefined, configurable: true });
}
