import { useState } from 'react';
import toast from 'react-hot-toast';
import { Button, ButtonGroup, CardList, CardListItem, Dialog, Dropdown, DropdownItem, IconButton, Input, PasswordInput, Spinner } from '../components/ui';
import { Icon } from '../components/ui/Icon';
import { useCreateUser, useCurrentUser, useDeleteUser, useSetRoles, useUsers } from '../hooks/useUserQueries';
import { iconMap } from '../lib/iconMap';
import { validatePassword } from '../lib/passwordValidation';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { ADMIN_ROLE, isAdmin, type UserInfo } from '../services/userService';

/** Every user, for an admin: creating them, making them admins, and deleting them. */
export function UsersPage() {
  const { data: users, isLoading } = useUsers();
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<UserInfo | null>(null);

  return (
    <div className="p-6 md:p-8">
      <div className="max-w-3xl mx-auto space-y-6">
        <div className="flex items-center justify-between gap-4">
          <h1 className="text-heading-md text-text-1-light dark:text-text-1-dark">Users</h1>
          <Button variant="primary" icon={<Icon icon={iconMap.add} size={18} />} onClick={() => setCreating(true)}>New user</Button>
        </div>
        <p className="text-body-sm text-text-4-light dark:text-text-4-dark">Admins create shelves and users and reach every shelf. Give everyone else access shelf by shelf, in a shelf's settings.</p>

        {isLoading && <Spinner />}
        {users && (
          <CardList>
            {users.map((user) => <UserRow key={user.id} user={user} onDelete={() => setDeleting(user)} />)}
          </CardList>
        )}
      </div>

      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
      {deleting && <DeleteUserDialog user={deleting} onClose={() => setDeleting(null)} />}
    </div>
  );
}

function UserRow({ user, onDelete }: { user: UserInfo; onDelete: () => void }) {
  const { data: me } = useCurrentUser();
  const setRoles = useSetRoles();
  const admin = isAdmin(user);

  function toggleAdmin(): Promise<void> {
    const roles = admin ? user.roles.filter((role) => role !== ADMIN_ROLE) : [...user.roles, ADMIN_ROLE];
    return runOrToast(() => setRoles.mutateAsync({ userId: user.id, roles }), 'Could not change the role');
  }

  return (
    <CardListItem>
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">
          <div className="truncate text-body-lg text-text-1-light dark:text-text-1-dark">
            {user.displayName}
            {me?.id === user.id && <span className="ml-2 text-body-md text-text-3-light dark:text-text-3-dark">(you)</span>}
          </div>
          <div className="truncate text-body-sm text-text-3-light dark:text-text-3-dark">
            {user.email}{admin && ' · admin'}
          </div>
        </div>
        <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${user.email}`} />}>
          <DropdownItem onClick={() => void toggleAdmin()}>{admin ? 'Make a user' : 'Make an admin'}</DropdownItem>
          <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={onDelete}>Delete</DropdownItem>
        </Dropdown>
      </div>
    </CardListItem>
  );
}

function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const createUser = useCreateUser();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<'user' | 'admin'>('user');
  const { error, setError, submit } = useSubmit('Could not create the user');

  function create(): Promise<void> {
    const passwordError = validatePassword(password);
    if (passwordError) {
      setError(passwordError);
      return Promise.resolve();
    }

    const request = {
      email: email.trim(),
      displayName: displayName.trim() || email.trim().split('@')[0],
      password,
      roles: role === 'admin' ? [ADMIN_ROLE] : [],
    };
    return submit(() => createUser.mutateAsync(request), (created) => {
      toast.success(`Created ${created.email}`);
      onClose();
    });
  }

  return (
    <Dialog open title="New user" onClose={onClose} onSubmit={() => void create()} submitLabel="Create" submitting={createUser.isPending} error={error}>
      <Input label="Email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="off" required />
      <Input label="Display name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="The email's first part when left empty" autoComplete="off" />
      <PasswordInput label="Password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" required />
      <ButtonGroup
        options={[
          { value: 'user', label: 'User' },
          { value: 'admin', label: 'Admin' },
        ]}
        value={role}
        onChange={setRole}
      />
    </Dialog>
  );
}

function DeleteUserDialog({ user, onClose }: { user: UserInfo; onClose: () => void }) {
  const deleteUser = useDeleteUser();
  const { error, submit } = useSubmit('Could not delete the user');

  function remove(): Promise<void> {
    return submit(() => deleteUser.mutateAsync(user.id), () => {
      toast.success(`Deleted ${user.email}`);
      onClose();
    });
  }

  return (
    <Dialog open title={`Delete ${user.email}?`} onClose={onClose} onSubmit={() => void remove()} submitLabel="Delete" submitVariant="danger" submitting={deleteUser.isPending} error={error}>
      <p className="text-body-md text-text-2-light dark:text-text-2-dark">Their access to every shelf ends at once, and so do their sessions. What they stored stays on the shelves.</p>
    </Dialog>
  );
}
