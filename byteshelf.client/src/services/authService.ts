import type { AuthResponse } from './authTypes';
import { AUTH_PATHS } from './authPaths';
import { ROUTES } from '../lib/routes';

// The session lives in localStorage, shared by every tab. A refresh token works once, and reusing a
// spent one ends every session in its family, so renewals run one at a time across tabs (Web Locks)
// and each first takes up a token another tab already renewed to.

const TOKEN_STORAGE_KEY = 'byteshelf_auth_token';
const EXPIRES_AT_STORAGE_KEY = 'byteshelf_auth_expires_at';
const REFRESH_TOKEN_STORAGE_KEY = 'byteshelf_auth_refresh_token';
/**
 * Kept with a session saved from a sign-in or renewal, both of which set the session's cookie for
 * opening files. A session saved before the server set that cookie lacks it.
 */
const COOKIE_SET_STORAGE_KEY = 'byteshelf_auth_cookie_set';
const RENEW_LOCK_NAME = 'byteshelf-token-renewal';

/** How long before the access token runs out it is renewed. */
const RENEW_AHEAD_MS = 60_000;

export function saveTokens(tokens: AuthResponse): void {
  localStorage.setItem(TOKEN_STORAGE_KEY, tokens.token);
  localStorage.setItem(EXPIRES_AT_STORAGE_KEY, tokens.expiresAt);
  localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, tokens.refreshToken);
  localStorage.setItem(COOKIE_SET_STORAGE_KEY, 'true');
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY);
}

export function getExpiresAt(): string | null {
  return localStorage.getItem(EXPIRES_AT_STORAGE_KEY);
}

function getRefreshToken(): string | null {
  return localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
}

export function clearTokens(): void {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
  localStorage.removeItem(EXPIRES_AT_STORAGE_KEY);
  localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  localStorage.removeItem(COOKIE_SET_STORAGE_KEY);
}

/** Whether a session is kept: its access token may have run out, but it can be renewed. */
export function hasSession(): boolean {
  return getRefreshToken() !== null;
}

/** Whether a storage event means the session was ended or replaced in another tab. */
export function isSessionStorageKey(key: string | null): boolean {
  return key === null || key === REFRESH_TOKEN_STORAGE_KEY;
}

export function redirectToLoginIfNeeded(): void {
  if (window.location.pathname !== ROUTES.LOGIN) {
    window.location.href = ROUTES.LOGIN;
  }
}

function isDue(): boolean {
  const expiresAt = getExpiresAt();
  return !expiresAt || new Date(expiresAt).getTime() - Date.now() <= RENEW_AHEAD_MS;
}

/** The last renewal queued in this tab, for when there are no Web Locks. */
let tabRenewTail: Promise<unknown> = Promise.resolve();

/**
 * Runs `work` while no other renewal runs, in this tab or another. Web Locks exist only in a secure
 * context (HTTPS or localhost); over plain HTTP renewals queue within this tab alone, so two tabs
 * renewing at the same moment can still spend the same refresh token, which ends that session.
 */
function withRenewLock<T>(work: () => Promise<T>): Promise<T> {
  // Absent at runtime outside a secure context, whatever the DOM types say.
  const locks: LockManager | undefined = navigator.locks;
  if (locks) {
    return locks.request(RENEW_LOCK_NAME, work) as Promise<T>;
  }

  const run = tabRenewTail.then(work, work);
  tabRenewTail = run.catch(() => undefined);
  return run;
}

/**
 * Answers an access token newer than `staleToken`: one another request or tab renewed to when it
 * is not due itself, else a renewed one. Null when the session has ended (it is then forgotten) or
 * the server cannot be reached.
 */
export function renewAccessToken(staleToken: string | null): Promise<string | null> {
  return withRenewLock(async () => {
    const current = getToken();
    if (current && current !== staleToken && !isDue()) {
      return current;
    }

    const refreshToken = getRefreshToken();
    if (!refreshToken) {
      return null;
    }

    let response: Response;
    try {
      response = await fetch(AUTH_PATHS.REFRESH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      });
    } catch {
      return null;
    }

    if (response.status === 400 || response.status === 401) {
      clearTokens();
      return null;
    }

    if (!response.ok) {
      return null;
    }

    const tokens = (await response.json()) as AuthResponse;
    saveTokens(tokens);
    return tokens.token;
  });
}

/** The access token to send, renewed first when it is about to run out; null when there is no session. */
export async function getFreshToken(): Promise<string | null> {
  const token = getToken();
  return token && !isDue() ? token : renewAccessToken(token);
}

/**
 * Whether the browser holds the session's cookie for opening files, renewing the session first when
 * its token is about to run out or it was saved before the server set that cookie. Cookies cleared by
 * hand, or a clock more than a minute behind the server's, are not seen here; a file opened then
 * answers 401 until the next renewal.
 */
export async function hasFreshCookie(): Promise<boolean> {
  const token = getToken();
  if (token && !isDue() && localStorage.getItem(COOKIE_SET_STORAGE_KEY)) return true;
  return (await renewAccessToken(token)) !== null;
}

/** Ends the session on the server, as far as it can, and forgets it here and in every tab. */
export async function signOut(): Promise<void> {
  // Under the renewal lock, so a renewal in flight cannot save a new session after this forgets it;
  // the token it renewed to is the one ended on the server.
  const refreshToken = await withRenewLock(async () => {
    const current = getRefreshToken();
    clearTokens();
    return current;
  });
  if (!refreshToken) {
    return;
  }

  try {
    await fetch(AUTH_PATHS.LOGOUT, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    });
  } catch {
    // The session runs out on its own.
  }
}
