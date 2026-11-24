const logged = new WeakSet<object>();

/** Logs why `failing` failed the first time it does, for what is asked the same whenever a page is drawn. */
export function logFailureOnce(failing: object, message: string, error: unknown): void {
  if (logged.has(failing)) return;
  logged.add(failing);
  console.error(message, error);
}
