// Safe localStorage access: reads and writes that swallow the exceptions thrown when storage is
// unavailable (private browsing, storage-restricted embeds, quota). Preference persistence is
// best-effort — the app must work identically without it.

export function readStoredValue(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writeStoredValue(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage unavailable — the preference just won't persist.
  }
}
