// Safe access to the browser's storage: reads and writes that swallow the exceptions thrown when it is
// unavailable (private browsing, storage-restricted embeds, quota). What is kept is best-effort: the app
// must work without it.

/** A storage, as it is reached: merely reading the property can throw. */
type Reach = () => Storage;

/** Shared by every tab, and kept. */
export const local: Reach = () => localStorage;
/** This tab's alone, and kept as long as it. */
export const session: Reach = () => sessionStorage;

export function readStoredValue(key: string, storage: Reach = local): string | null {
  try {
    return storage().getItem(key);
  } catch {
    return null;
  }
}

export function writeStoredValue(key: string, value: string, storage: Reach = local): void {
  try {
    storage().setItem(key, value);
  } catch {
    // Storage unavailable: the value just won't persist.
  }
}

export function removeStoredValue(key: string, storage: Reach = local): void {
  try {
    storage().removeItem(key);
  } catch {
    // Nothing was kept.
  }
}
