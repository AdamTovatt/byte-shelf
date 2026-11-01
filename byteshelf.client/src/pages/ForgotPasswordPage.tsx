import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Button, Input } from '../ui';
import { Notice } from '../components/Notice';
import { AuthLayout } from '../components/layout/AuthLayout';
import { requestPasswordReset } from '../services/passwordResetService';
import { useSubmit } from '../hooks/useSubmit';
import { ROUTES } from '../lib/routes';

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [submitted, setSubmitted] = useState(false);
  const { error, submitting, submit } = useSubmit('Something went wrong. Please try again.');

  const handleSubmit = async (e: FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();
    if (!email.trim()) return;

    await submit(() => requestPasswordReset(email.trim()), () => setSubmitted(true));
  };

  return (
    <AuthLayout title="Reset your password">
      {submitted ? (
        <div className="flex flex-col gap-6">
          <Notice variant="info">
            If an account exists for {email.trim()}, a reset link has been sent. Check your inbox.
          </Notice>
          <Link to={ROUTES.LOGIN} className="text-body-md text-muted hover:text-fg hover:underline text-center">
            Back to sign in
          </Link>
        </div>
      ) : (
        <form onSubmit={handleSubmit} className="flex flex-col gap-6">
          {error && (
            <Notice variant="danger">
              {error}
            </Notice>
          )}

          <Input
            label="Email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Enter your email"
            disabled={submitting}
            required
            autoComplete="email"
          />

          <Button
            type="submit"
            variant="main"
            size="lg"
            className="w-full"
            disabled={submitting || !email.trim()}
            loading={submitting}
          >
            Send reset link
          </Button>

          <Link to={ROUTES.LOGIN} className="text-body-md text-muted hover:text-fg hover:underline text-center">
            Back to sign in
          </Link>
        </form>
      )}
    </AuthLayout>
  );
}
