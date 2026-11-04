/** The admin the browser test's server starts with, and signs in as. */
export const ADMIN_EMAIL = 'admin@e2e.test';
export const ADMIN_PASSWORD = 'password-for-e2e';

/**
 * Fixed rather than taken from ASPNETCORE_URLS, so a developer's own setting cannot move the server
 * out from under the test; BYTESHELF_E2E_PORT picks another when this one is taken.
 */
export const PORT = Number(process.env.BYTESHELF_E2E_PORT ?? 5397);
