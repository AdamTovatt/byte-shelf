// The passwords this tab has unlocked, so a file asks for its password once: kept in sessionStorage,
// which lives as long as the tab, and forgotten on sign-out. A tab a script opens starts with a copy of
// its opener's sessionStorage, so the app opens tabs through `openTabWithoutPasswords`, and no other
// tab holds them. An encrypted folder's password is kept under the folder, a file encrypted on its own
// under the file.

const PREFIX = 'byteshelf.password.';

/** Where an encrypted folder's password is kept. */
export function folderPasswordKey(shelf: string, folderId: string): string {
  return `${PREFIX}${shelf}/${folderId}`;
}

/** Where an encrypted file's password is kept: under its encrypted folder when it is in one, else under the file. */
export function filePasswordKey(shelf: string, folderId: string | undefined, fileId: string): string {
  return folderPasswordKey(shelf, folderId ?? fileId);
}

export function rememberedPassword(key: string): string | null {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

export function rememberPassword(key: string, password: string): void {
  try {
    sessionStorage.setItem(key, password);
  } catch {
    // Storage unavailable: the password is asked for again next time.
  }
}

export function forgetPassword(key: string): void {
  try {
    sessionStorage.removeItem(key);
  } catch {
    // Nothing was kept.
  }
}

/** Forgets every password this tab kept, as signing out does. */
export function forgetPasswords(): void {
  try {
    removePasswords(sessionStorage);
  } catch {
    // Nothing was kept.
  }
}

function removePasswords(storage: Storage): void {
  for (const key of Object.keys(storage)) {
    if (key.startsWith(PREFIX)) storage.removeItem(key);
  }
}

/**
 * Opens an empty tab, as `window.open('', '_blank')` does, with the passwords taken out of the copy of
 * this tab's sessionStorage it starts with: the page it goes on to is the app's origin, and it would
 * keep them past this tab's sign-out. Null when the browser blocked it, or when the copy could not be
 * reached, so the tab is closed rather than left holding them.
 */
export function openTabWithoutPasswords(): Window | null {
  const tab = window.open('', '_blank');
  if (!tab) return null;
  try {
    removePasswords(tab.sessionStorage);
    return tab;
  } catch {
    tab.close();
    return null;
  }
}
