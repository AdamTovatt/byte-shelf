import type { AuthResponse } from './authTypes';
import { AUTH_PATHS } from './authPaths';

// The session lives in localStorage, shared by every tab. A refresh token works once, and reusing a
// spent one ends every session in its family, so renewals run one at a time across tabs (Web Locks)
// and each first takes up a token another tab already renewed to.

const TOKEN_STORAGE_KEY = 'byteshelf_auth_token';
const EXPIRES_AT_STORAGE_KEY = 'byteshelf_auth_expires_at';
const REFRESH_TOKEN_STORAGE_KEY = 'byteshelf_auth_refresh_token';
const RENEW_LOCK_NAME = 'byteshelf-token-renewal';

/** How long before the access token runs out it is renewed. */
const RENEW_AHEAD_MS = 60_000;

export function saveTokens(tokens: AuthResponse): void {
  localStorage.setItem(TOKEN_STORAGE_KEY, tokens.token);
  localStorage.setItem(EXPIRES_AT_STORAGE_KEY, tokens.expiresAt);
  localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, tokens.refreshToken);
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
  if (window.location.pathname !== '/login') {
    window.location.href = '/login';
  }
}

function isDue(): boolean {
  const expiresAt = getExpiresAt();
  return !expiresAt || new Date(expiresAt).getTime() - Date.now() <= RENEW_AHEAD_MS;
}

/** Runs `work` while no other renewal runs, in this tab or another. */
function withRenewLock<T>(work: () => Promise<T>): Promise<T> {
  return navigator.locks.request(RENEW_LOCK_NAME, work) as Promise<T>;
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

/** Ends the session on the server, as far as it can, and forgets it here and in every tab. */
export async function signOut(): Promise<void> {
  const refreshToken = getRefreshToken();
  clearTokens();
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
