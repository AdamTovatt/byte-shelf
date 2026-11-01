import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Button, Input } from '../ui';
import { Notice } from '../components/Notice';
import { AuthLayout } from '../components/layout/AuthLayout';
import { requestPasswordReset } from '../services/passwordResetService';

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = async (e: FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();
    if (!email.trim()) return;

    setError('');
    setIsLoading(true);

    try {
      await requestPasswordReset(email.trim());
      setSubmitted(true);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Something went wrong. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <AuthLayout title="Reset your password">
      {submitted ? (
        <div className="flex flex-col gap-6">
          <Notice variant="info">
            If an account exists for {email.trim()}, a reset link has been sent. Check your inbox.
          </Notice>
          <Link to="/login" className="text-body-md text-muted hover:text-fg hover:underline text-center">
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
            disabled={isLoading}
            required
            autoComplete="email"
          />

          <Button
            type="submit"
            variant="main"
            size="lg"
            className="w-full"
            disabled={isLoading || !email.trim()}
            loading={isLoading}
          >
            Send reset link
          </Button>

          <Link to="/login" className="text-body-md text-muted hover:text-fg hover:underline text-center">
            Back to sign in
          </Link>
        </form>
      )}
    </AuthLayout>
  );
}
