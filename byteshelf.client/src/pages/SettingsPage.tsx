import { useState, type FormEvent } from 'react';
import toast from 'react-hot-toast';
import { Button, ButtonGroup, Icon, PageLayout, getErrorMessage, iconMap, useTheme } from '../ui';
import { PasswordInput } from '../components/PasswordInput';
import { SectionTitle } from '../components/SectionTitle';
import { useChangeOwnPassword } from '../hooks/useUserQueries';
import { validatePassword } from '../lib/passwordValidation';

/** The signed-in user's own settings: the theme and their password. Signing out is in the account menu. */
export function SettingsPage() {
  const { theme, setTheme } = useTheme();

  return (
    <PageLayout className="space-y-10">
      <h1 className="text-heading-md text-fg">Settings</h1>

      <section className="flex flex-wrap items-center justify-between gap-3">
        <SectionTitle>Theme</SectionTitle>
        <ButtonGroup
          options={[
            { value: 'system', icon: <Icon icon={iconMap.themeSystem} size={18} />, tooltip: 'System' },
            { value: 'light', icon: <Icon icon={iconMap.themeLight} size={18} />, tooltip: 'Light' },
            { value: 'dark', icon: <Icon icon={iconMap.themeDark} size={18} />, tooltip: 'Dark' },
          ]}
          value={theme}
          onChange={setTheme}
          ariaLabel="Theme"
        />
      </section>

      <ChangePasswordSection />
    </PageLayout>
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
      setError(getErrorMessage(caught, 'Could not update the password'));
    }
  };

  return (
    <section className="space-y-4">
      <SectionTitle>Change password</SectionTitle>
      <form onSubmit={(event) => void handleSubmit(event)} className="space-y-4">
        <PasswordInput
          label="Current password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          disabled={changePassword.isPending}
          required
          autoComplete="current-password"
        />
        <PasswordInput
          label="New password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          disabled={changePassword.isPending}
          required
          autoComplete="new-password"
        />
        <PasswordInput
          label="Confirm the new password"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          disabled={changePassword.isPending}
          required
          autoComplete="new-password"
          error={error}
        />
        <Button type="submit" variant="main" loading={changePassword.isPending}>
          Update password
        </Button>
      </form>
    </section>
  );
}
