import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Button } from '../ui';
import { Notice } from '../components/Notice';
import { PasswordInput } from '../components/PasswordInput';
import { AuthLayout } from '../components/layout/AuthLayout';
import { completePasswordReset } from '../services/passwordResetService';
import { validateNewPassword } from '../lib/passwordValidation';
import { useSubmit } from '../hooks/useSubmit';
import { ROUTES } from '../lib/routes';

// Landing page for the emailed reset link (/reset-password?token=…). Consumes the single-use
// token and sets the new password; a successful reset also revokes every other session.
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const token = searchParams.get('token') ?? '';

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const { error, setError, submitting, submit } = useSubmit('Something went wrong. Please try again.');

  const handleSubmit = async (e: FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();

    const validationError = validateNewPassword(newPassword, confirmPassword);
    if (validationError) {
      setError(validationError);
      return;
    }

    await submit(() => completePasswordReset(token, newPassword), () => navigate(ROUTES.LOGIN, { replace: true }));
  };

  return (
    <AuthLayout title="Choose a new password">
      {!token ? (
        <div className="flex flex-col gap-6">
          <Notice variant="danger">
            This reset link is missing its token. Use the link from the email, or request a new one.
          </Notice>
          <Link to={ROUTES.FORGOT_PASSWORD} className="text-body-md text-muted hover:text-fg hover:underline text-center">
            Request a new link
          </Link>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="flex flex-col gap-6">
          {error && (
            <Notice variant="danger">
              {error}
            </Notice>
          )}

          <PasswordInput
            label="New password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder="Enter new password"
            disabled={submitting}
            required
            autoComplete="new-password"
          />

          <PasswordInput
            label="Confirm password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="Confirm new password"
            disabled={submitting}
            required
            autoComplete="new-password"
          />

          <Button
            type="submit"
            variant="main"
            size="lg"
            className="w-full"
            disabled={submitting || !newPassword || !confirmPassword}
            loading={submitting}
          >
            Set new password
          </Button>
        </form>
      )}
    </AuthLayout>
  );
}
