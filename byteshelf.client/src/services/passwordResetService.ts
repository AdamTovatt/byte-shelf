// Password reset runs unauthenticated, so it uses plain fetch instead of the authed apiClient —
// a stale token in storage must not interfere with the reset flow.

export async function requestPasswordReset(email: string): Promise<void> {
  // The endpoint is enumeration-safe: it returns 200 whether or not the email exists, so there
  // is no failure case to surface beyond a network/server error.
  const response = await fetch('/api/PasswordReset/request', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  });

  if (!response.ok) {
    throw new Error('Failed to request a password reset. Please try again.');
  }
}

export async function completePasswordReset(token: string, newPassword: string): Promise<boolean> {
  const response = await fetch('/api/PasswordReset/complete', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token, newPassword }),
  });

  return response.ok;
}
