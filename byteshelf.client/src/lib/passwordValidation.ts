// Mirrors the backend rule (`PasswordPolicy.MinimumLength`). Kept in one place so the
// password forms (login-adjacent reset, own change, create user) can't drift.
export const MIN_PASSWORD_LENGTH = 8;

export function validatePassword(password: string): string | undefined {
  if (!password) {
    return 'Password is required';
  }
  if (password.length < MIN_PASSWORD_LENGTH) {
    return `Password must be at least ${MIN_PASSWORD_LENGTH} characters long`;
  }
  return undefined;
}

/** For a form that asks for a new password twice: why it cannot be used, or undefined when it can. */
export function validateNewPassword(newPassword: string, confirmPassword: string): string | undefined {
  return validatePassword(newPassword) ?? (newPassword !== confirmPassword ? 'Passwords do not match' : undefined);
}
