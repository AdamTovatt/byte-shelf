import { useState } from 'react';
import toast from 'react-hot-toast';
import { Button, ButtonGroup, Card, Dropdown, DropdownItem, Icon, IconButton, Input, PageLayout, Spinner, iconMap } from '../ui';
import { FormDialog } from '../components/FormDialog';
import { PasswordInput } from '../components/PasswordInput';
import { useCreateUser, useCurrentUser, useDeleteUser, useSetRoles, useUsers } from '../hooks/useUserQueries';
import { useGrantsByShelf, useRemoveGrant, useSetGrant } from '../hooks/useAccessQueries';
import { useShelves } from '../hooks/useShelfQueries';
import { ACCESS_OPTIONS } from '../lib/access';
import { validatePassword } from '../lib/passwordValidation';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import { ADMIN_ROLE, isAdmin, type UserInfo } from '../services/userService';
import type { ShelfAccessPermission } from '../services/accessService';

/** A user's access to one shelf, as the users page shows it. */
interface UserShelf {
  shelf: string;
  permission: ShelfAccessPermission;
}

/** Every user, for an admin: creating them, which shelves they reach, making them admins, and deleting them. */
export function UsersPage() {
  const { data: users, isLoading } = useUsers();
  const { data: shelves = [] } = useShelves();
  const shelfNames = shelves.map((shelf) => shelf.name);
  const grantsByShelf = useGrantsByShelf(shelfNames);
  const [creating, setCreating] = useState(false);
  const [editingAccess, setEditingAccess] = useState<UserInfo | null>(null);
  const [deleting, setDeleting] = useState<UserInfo | null>(null);

  function shelvesOf(user: UserInfo): UserShelf[] {
    return shelfNames.flatMap((shelf) => {
      const grant = grantsByShelf.get(shelf)?.find((candidate) => candidate.userId === user.id);
      return grant ? [{ shelf, permission: grant.permission }] : [];
    });
  }

  return (
    <PageLayout className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-heading-md text-fg">Users</h1>
          <p className="text-body-md text-muted">Admins create shelves and users and reach every shelf. Everyone else reaches the shelves they are given.</p>
        </div>
        <Button variant="main" icon={<Icon icon={iconMap.add} size={16} />} onClick={() => setCreating(true)}>New user</Button>
      </div>

      {isLoading && <Spinner />}
      {users && (
        <Card variant="elevated" padding="none" className="divide-y divide-border">
          {users.map((user) => (
            <UserRow
              key={user.id}
              user={user}
              shelves={shelvesOf(user)}
              onEditAccess={() => setEditingAccess(user)}
              onDelete={() => setDeleting(user)}
            />
          ))}
        </Card>
      )}

      {creating && <CreateUserDialog onClose={() => setCreating(false)} />}
      {editingAccess && <UserAccessDialog user={editingAccess} shelves={shelfNames} access={shelvesOf(editingAccess)} onClose={() => setEditingAccess(null)} />}
      {deleting && <DeleteUserDialog user={deleting} onClose={() => setDeleting(null)} />}
    </PageLayout>
  );
}

function describeShelves(user: UserInfo, shelves: UserShelf[]): string {
  if (isAdmin(user)) return 'Every shelf, as an admin';
  if (shelves.length === 0) return 'No shelves yet';
  return shelves.map(({ shelf, permission }) => `${shelf} (${permission === 'readWrite' ? 'read and write' : 'read'})`).join(', ');
}

function UserRow({ user, shelves, onEditAccess, onDelete }: { user: UserInfo; shelves: UserShelf[]; onEditAccess: () => void; onDelete: () => void }) {
  const { data: me } = useCurrentUser();
  const setRoles = useSetRoles();
  const admin = isAdmin(user);

  function toggleAdmin(): Promise<void> {
    const roles = admin ? user.roles.filter((role) => role !== ADMIN_ROLE) : [...user.roles, ADMIN_ROLE];
    return runOrToast(() => setRoles.mutateAsync({ userId: user.id, roles }), 'Could not change the role');
  }

  return (
    <div className="flex items-start gap-3 px-4 py-3">
      <div className="min-w-0 flex-1 space-y-0.5">
        <div className="truncate text-body-md font-medium text-fg">
          {user.displayName}
          {me?.id === user.id && <span className="ml-2 font-normal text-muted">(you)</span>}
        </div>
        <div className="truncate text-body-sm text-muted">{user.email}{admin && ' · admin'}</div>
        <div className="text-body-sm text-muted">{describeShelves(user, shelves)}</div>
      </div>
      {!admin && (
        <Button size="sm" variant="secondary" onClick={onEditAccess} aria-label={`Change ${user.email}'s shelves`}>
          Shelves
        </Button>
      )}
      <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Actions for ${user.email}`} />}>
        <DropdownItem icon={<Icon icon={iconMap.admin} size={16} />} onClick={() => void toggleAdmin()}>{admin ? 'Make a user' : 'Make an admin'}</DropdownItem>
        <DropdownItem icon={<Icon icon={iconMap.delete} size={16} />} variant="danger" onClick={onDelete}>Delete</DropdownItem>
      </Dropdown>
    </div>
  );
}

const NO_ACCESS = 'none';
type AccessChoice = ShelfAccessPermission | typeof NO_ACCESS;
const ACCESS_CHOICES: { value: AccessChoice; label: string }[] = [{ value: NO_ACCESS, label: 'None' }, ...ACCESS_OPTIONS];

/** One user's access on every shelf, each changed as it is picked, the same change a shelf's settings make. */
function UserAccessDialog({ user, shelves, access, onClose }: { user: UserInfo; shelves: string[]; access: UserShelf[]; onClose: () => void }) {
  const setGrant = useSetGrant();
  const removeGrant = useRemoveGrant();

  function change(shelf: string, choice: AccessChoice): Promise<void> {
    return runOrToast(
      () => (choice === NO_ACCESS
        ? removeGrant.mutateAsync({ shelf, userId: user.id })
        : setGrant.mutateAsync({ shelf, userId: user.id, permission: choice })),
      `Could not change ${user.email}'s access to ${shelf}`,
    );
  }

  return (
    <FormDialog open title={`${user.displayName}'s shelves`} onClose={onClose}>
      {shelves.length === 0 && <p className="text-body-md text-muted">There are no shelves yet.</p>}
      <div className="divide-y divide-border">
        {shelves.map((shelf) => (
          <div key={shelf} className="flex flex-wrap items-center justify-between gap-3 py-2">
            <span className="min-w-0 truncate text-body-md text-fg">{shelf}</span>
            <ButtonGroup
              size="sm"
              options={ACCESS_CHOICES}
              value={access.find((candidate) => candidate.shelf === shelf)?.permission ?? NO_ACCESS}
              onChange={(choice) => void change(shelf, choice)}
              ariaLabel={`Access to ${shelf}`}
            />
          </div>
        ))}
      </div>
    </FormDialog>
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
    <FormDialog open title="New user" onClose={onClose} onSubmit={() => void create()} submitLabel="Create" submitting={createUser.isPending} error={error}>
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
        ariaLabel="Role"
      />
    </FormDialog>
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
    <FormDialog open title={`Delete ${user.email}?`} onClose={onClose} onSubmit={() => void remove()} submitLabel="Delete" submitVariant="danger" submitting={deleteUser.isPending} error={error}>
      <p className="text-body-md text-fg">Their access to every shelf ends at once, and so do their sessions. What they stored stays on the shelves.</p>
    </FormDialog>
  );
}
