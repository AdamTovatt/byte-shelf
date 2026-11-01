import type { components } from '../types/api';
import { PASSWORD_RESET_PATHS } from './authPaths';

// Password reset runs unauthenticated, so it uses plain fetch instead of the authed apiClient —
// a stale token in storage must not interfere with the reset flow.

type RequestPasswordResetRequest = components['schemas']['RequestPasswordResetRequest'];
type CompletePasswordResetRequest = components['schemas']['CompletePasswordResetRequest'];

function post(path: string, body: RequestPasswordResetRequest | CompletePasswordResetRequest): Promise<Response> {
  return fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function requestPasswordReset(email: string): Promise<void> {
  // The endpoint is enumeration-safe: it answers 204 whether or not the email exists, so there
  // is no failure case to surface beyond a network/server error.
  const response = await post(PASSWORD_RESET_PATHS.REQUEST, { email });
  if (!response.ok) {
    throw new Error('Failed to request a password reset. Please try again.');
  }
}

/** Sets the new password; throws with the reason to show when the server refuses. */
export async function completePasswordReset(token: string, newPassword: string): Promise<void> {
  const response = await post(PASSWORD_RESET_PATHS.COMPLETE, { token, newPassword });
  if (response.status === 400) {
    throw new Error('This reset link is invalid or has expired. Request a new one.');
  }

  if (!response.ok) {
    throw new Error('Something went wrong. Please try again.');
  }
}
