import { useCallback, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Button, Icon, Input, iconMap } from '../ui';
import { Notice } from '../components/Notice';
import { AuthLayout } from '../components/layout/AuthLayout';
import type { AuthResponse } from '../services/authTypes';
import { AUTH_PATHS } from '../services/authPaths';
import { ROUTES } from '../lib/routes';
import { usePasskeySignIn } from '../hooks/usePasskeySignIn';

/**
 * The one line for every passkey failure but a closed prompt, a rate limit and an unreachable server
 * included: unlike the password form's, it does not vary by cause, as issue #15 settles.
 */
const PASSKEY_FAILED = 'Could not sign in with that passkey';

/** Signing in; App's route sends a signed-in user home instead. */
export function LoginPage() {
  const [email, setEmail] = useState<string>('');
  const [password, setPassword] = useState<string>('');
  const [error, setError] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const signedIn = useCallback((data: AuthResponse) => {
    login(data);
    navigate(ROUTES.HOME);
  }, [login, navigate]);

  // A passkey that fails leaves the password form as it is.
  const passkey = usePasskeySignIn({ onSignedIn: signedIn, onFailed: () => setError(PASSKEY_FAILED) });

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

      signedIn(await response.json() as AuthResponse);
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
          // "webauthn" lets the browser offer the saved passkeys here as suggestions.
          autoComplete="username webauthn"
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

        {passkey.available && (
          <Button
            type="button"
            variant="secondary"
            size="lg"
            className="w-full"
            icon={<Icon icon={iconMap.key} size={16} />}
            disabled={isLoading}
            loading={passkey.pending}
            onClick={() => {
              setError('');
              void passkey.signIn();
            }}
          >
            Sign in with passkey
          </Button>
        )}

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
