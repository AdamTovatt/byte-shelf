import { readStoredValue, removeStoredValue, session, writeStoredValue } from './storage';

// A toast for the page to show once it has started anew, kept for this tab alone.

const KEY = 'byteshelf.startup_toast';

export function leaveStartupToast(text: string): void {
  writeStoredValue(KEY, text, session);
}

/** The text left for this page, once; null when there is none. */
export function takeStartupToast(): string | null {
  const text = readStoredValue(KEY, session);
  removeStoredValue(KEY, session);
  return text;
}
