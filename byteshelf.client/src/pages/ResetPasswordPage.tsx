import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Banner, PasswordInput } from '../components/ui';
import { AuthLayout } from '../components/layout/AuthLayout';
import { completePasswordReset } from '../services/passwordResetService';
import { validatePassword } from '../lib/passwordValidation';

// Landing page for the emailed reset link (/reset-password?token=…). Consumes the single-use
// token and sets the new password; a successful reset also revokes every other session.
export function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const token = searchParams.get('token') ?? '';

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();

    const validationError = validatePassword(newPassword);
    if (validationError) {
      setError(validationError);
      return;
    }

    if (newPassword !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    setError('');
    setIsLoading(true);

    try {
      const success = await completePasswordReset(token, newPassword);
      if (!success) {
        setError('This reset link is invalid or has expired. Request a new one.');
        return;
      }

      navigate('/login', { replace: true });
    } catch {
      setError('Something went wrong. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <AuthLayout title="Choose a new password">
      {!token ? (
        <div className="flex flex-col gap-6">
          <Banner variant="danger" inline>
            This reset link is missing its token. Use the link from the email, or request a new one.
          </Banner>
          <Link to="/forgot-password" className="text-body-md text-brand-primary-light dark:text-brand-primary-dark hover:underline text-center">
            Request a new link
          </Link>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="flex flex-col gap-6">
          {error && (
            <Banner variant="danger" inline>
              {error}
            </Banner>
          )}

          <PasswordInput
            label="New password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder="Enter new password"
            disabled={isLoading}
            required
            autoComplete="new-password"
          />

          <PasswordInput
            label="Confirm password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="Confirm new password"
            disabled={isLoading}
            required
            autoComplete="new-password"
          />

          <Button
            type="submit"
            variant="brand"
            size="md"
            className="w-full h-12"
            disabled={isLoading || !newPassword || !confirmPassword}
            loading={isLoading}
          >
            Set new password
          </Button>
        </form>
      )}
    </AuthLayout>
  );
}
