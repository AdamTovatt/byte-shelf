import type { paths } from '../types/api';

// The requests that go around the typed apiClient, because they run without a session or manage it.
// Each path is checked against the server's API description, so a wrong one fails to compile.
//
// They stay hand-written requests for now: the generated AuthResponse marks its fields optional, so
// moving them onto openapi-fetch waits for the server's OpenAPI to mark those fields required.

export const AUTH_PATHS = {
  LOGIN: '/api/auth/login',
  REFRESH: '/api/auth/refresh',
  LOGOUT: '/api/auth/logout',
} as const satisfies Record<string, keyof paths>;

export const PASSWORD_RESET_PATHS = {
  REQUEST: '/api/password-reset/request',
  COMPLETE: '/api/password-reset/complete',
} as const satisfies Record<string, keyof paths>;
