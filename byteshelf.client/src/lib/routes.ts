// The app's top-level browser routes; a shelf's own are built in shelfPaths. The server writes
// RESET_PASSWORD into the links it emails, so that one must not change on its own.
export const ROUTES = {
  HOME: '/',
  LOGIN: '/login',
  FORGOT_PASSWORD: '/forgot-password',
  RESET_PASSWORD: '/reset-password',
  SETTINGS: '/settings',
  USERS: '/users',
  REPLICATION: '/replication',
} as const;
