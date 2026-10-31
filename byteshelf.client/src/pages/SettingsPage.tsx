import { useState, type FormEvent } from 'react';
import toast from 'react-hot-toast';
import { iconMap } from '../lib/iconMap';
import { Icon } from '../components/ui/Icon';
import { PasswordInput, Button, ButtonGroup } from '../components/ui';
import { useTheme } from '../contexts/ThemeContext';
import { useAuth } from '../contexts/AuthContext';
import { useChangeOwnPassword, useCurrentUser } from '../hooks/useUserQueries';
import { getErrorMessage } from '../lib/utils';
import { validatePassword } from '../lib/passwordValidation';

export function SettingsPage() {
  const { theme, setTheme } = useTheme();

  return (
    <div className="p-6 md:p-8">
      <div className="max-w-2xl mx-auto space-y-10">
        <h1 className="text-heading-md text-text-1-light dark:text-text-1-dark">Settings</h1>

        <section className="flex items-center justify-between">
          <div className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">
            Theme
          </div>
          <ButtonGroup
            options={[
              { value: 'system', icon: <Icon icon={iconMap.themeSystem} size={18} />, tooltip: 'System' },
              { value: 'light', icon: <Icon icon={iconMap.themeLight} size={18} />, tooltip: 'Light' },
              { value: 'dark', icon: <Icon icon={iconMap.themeDark} size={18} />, tooltip: 'Dark' },
            ]}
            value={theme}
            onChange={setTheme}
          />
        </section>

        <ChangePasswordSection />

        <AccountSection />
      </div>
    </div>
  );
}

function ChangePasswordSection() {
  const changePassword = useChangeOwnPassword();
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<string | undefined>(undefined);

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

    setError(undefined);

    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      toast.success('Password updated');
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (caught) {
      const message = getErrorMessage(caught, 'Failed to update password');
      toast.error(message);
      setError(message);
    }
  };

  return (
    <section className="space-y-3">
      <div className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">
        Change password
      </div>
      <form onSubmit={handleSubmit} className="space-y-3">
        <PasswordInput
          label="Current password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          placeholder="Enter current password"
          disabled={changePassword.isPending}
          required
          autoComplete="current-password"
        />
        <PasswordInput
          label="New password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          placeholder="Enter new password"
          disabled={changePassword.isPending}
          required
          autoComplete="new-password"
        />
        <PasswordInput
          label="Confirm password"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          placeholder="Confirm new password"
          disabled={changePassword.isPending}
          required
          autoComplete="new-password"
          error={error}
        />
        <Button
          type="submit"
          variant="primary"
          loading={changePassword.isPending}
          disabled={changePassword.isPending}
          className="w-full md:w-auto"
        >
          Update password
        </Button>
      </form>
    </section>
  );
}

function AccountSection() {
  const { data: user } = useCurrentUser();
  const { logout } = useAuth();

  return (
    <section className="space-y-3">
      <div className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">
        Account
      </div>
      <div className="flex items-center justify-between gap-4">
        <div className="text-body-lg text-text-2-light dark:text-text-2-dark">
          Signed in as <span className="font-medium">{user?.displayName ?? '—'}</span>
          {user?.email && (
            <span className="text-text-3-light dark:text-text-3-dark"> ({user.email})</span>
          )}
        </div>
        <Button
          variant="outlineDanger"
          onClick={() => void logout()}
          icon={<Icon icon={iconMap.logout} size={18} />}
        >
          Log out
        </Button>
      </div>
    </section>
  );
}
