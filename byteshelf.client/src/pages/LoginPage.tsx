import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Button, Input } from '../ui';
import { Notice } from '../components/Notice';
import { AuthLayout } from '../components/layout/AuthLayout';
import type { AuthResponse } from '../services/authTypes';
import { AUTH_PATHS } from '../services/authPaths';
import { ROUTES } from '../lib/routes';

/** Signing in; App's route sends a signed-in user home instead. */
export function LoginPage() {
  const [email, setEmail] = useState<string>('');
  const [password, setPassword] = useState<string>('');
  const [error, setError] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: FormEvent<HTMLFormElement>): Promise<void> => {
    e.preventDefault();
    e.stopPropagation();
    if (!email.trim() || !password.trim()) {
      setError('Please enter both email and password');
      return;
    }

    setError('');
    setIsLoading(true);

    try {
      // The library's login request field is named `username`, but the credential it carries in
      // this stack is the email address.
      const response = await fetch(AUTH_PATHS.LOGIN, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: email, password }),
      });

      if (!response.ok) {
        setError(response.status === 401
          ? 'Invalid email or password'
          : response.status === 429
            ? 'Too many attempts; wait a minute and try again'
            : `Could not sign in: the server answered ${response.status}`);
        setIsLoading(false);
        return;
      }

      const data: AuthResponse = await response.json();
      login(data);
      navigate(ROUTES.HOME);
    } catch {
      setError('Could not reach the server');
      setIsLoading(false);
    }
  };

  return (
    <AuthLayout>
      <form onSubmit={handleSubmit} className="flex flex-col gap-6">
        {error && (
          <Notice variant="danger">
            {error}
          </Notice>
        )}

        <Input
          label="Email"
          type="email"
          id="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="Enter your email"
          disabled={isLoading}
          required
          autoComplete="email"
        />

        <Input
          label="Password"
          type="password"
          id="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="Enter your password"
          disabled={isLoading}
          required
          autoComplete="current-password"
        />

        <Button
          type="submit"
          variant="main"
          size="lg"
          className="w-full mt-2"
          disabled={isLoading || !email.trim() || !password.trim()}
          loading={isLoading}
        >
          Sign in
        </Button>

        <Link
          to={ROUTES.FORGOT_PASSWORD}
          className="text-body-md text-muted hover:text-fg hover:underline text-center"
        >
          Forgot your password?
        </Link>
      </form>
    </AuthLayout>
  );
}
