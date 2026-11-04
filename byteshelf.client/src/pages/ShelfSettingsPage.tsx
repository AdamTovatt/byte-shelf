import { useState, type FormEvent, type ReactNode } from 'react';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { Button, ButtonGroup, CopyField, Icon, IconButton, Input, PAGE_CONTENT_WIDTH, RouteTab, TabGroup, cn, getErrorMessage, iconMap, type IconComponent } from '../ui';
import { TabbedPageShell } from '../components/layout/TabbedPageShell';
import { FormDialog } from '../components/FormDialog';
import { LoadError } from '../components/LoadError';
import { QuotaField } from '../components/QuotaField';
import { SectionTitle } from '../components/SectionTitle';
import { CardList } from '../components/CardList';
import { ShelfHeader } from '../components/ShelfHeader';
import { useCreateKey, useDeleteKey, useGrants, useKeys, useRemoveGrant, useSetGrant } from '../hooks/useAccessQueries';
import { useDeleteShelf, useShelf, useUpdateShelf } from '../hooks/useShelfQueries';
import { useUsers } from '../hooks/useUserQueries';
import { formatQuota, formatTime, parseQuota, tryParseQuota, type QuotaUnit } from '../lib/format';
import { ACCESS_OPTIONS, KEY_OPTIONS, keyPermissionLabel } from '../lib/access';
import { SHELF_SETTINGS_TABS, shelfSettingsRoute, type ShelfSettingsTab } from '../lib/shelfPaths';
import { ROUTES } from '../lib/routes';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import type { CreatedShelfKey, ShelfAccessGrant, ShelfAccessPermission, ShelfKeyInfo, ShelfKeyPermission } from '../services/accessService';
import type { ShelfInfo } from '../services/shelfService';

/** Each tab: its name, its icon, and what it shows. Typed by the tab list, so none can be left out. */
const TABS: Record<ShelfSettingsTab, { label: string; icon: IconComponent; Content: (props: { shelf: string }) => ReactNode }> = {
  general: { label: 'General', icon: iconMap.settings, Content: GeneralTab },
  access: { label: 'Access', icon: iconMap.users, Content: GrantsSection },
  keys: { label: 'Keys', icon: iconMap.key, Content: KeysSection },
};

/**
 * An admin's settings for one shelf, as tabs that are routes, so each can be linked to and the back
 * button steps between them: General (name, quota and deleting it), Access (who may use it) and Keys
 * (which programs may). The tab comes from the route (`SHELF_ROUTE_PATTERNS.settings`); none, or one
 * not in the list, goes to General.
 */
export function ShelfSettingsPage() {
  const params = useParams();
  const shelf = params.shelf!;
  const tab = SHELF_SETTINGS_TABS.find((each) => each === params.tab);
  if (!tab) return <Navigate to={shelfSettingsRoute(shelf, SHELF_SETTINGS_TABS[0])} replace />;
  const { Content } = TABS[tab];

  return (
    <TabbedPageShell
      header={
        // The page's own padding and width, so the breadcrumbs sit where they do on the files and
        // trash pages; the tabs hang off the band's bottom rule. Written out rather than a
        // PageLayout: that pads all four sides, and this band alone wants no padding below.
        <div className="shrink-0 border-b border-border bg-panel px-6 pt-6 md:px-8 md:pt-8">
          <div className={cn(PAGE_CONTENT_WIDTH, 'mx-auto space-y-4')}>
            <ShelfHeader shelf={shelf} view="settings" />
            <TabGroup>
              {SHELF_SETTINGS_TABS.map((each) => (
                <RouteTab key={each} label={TABS[each].label} icon={TABS[each].icon} href={shelfSettingsRoute(shelf, each)} />
              ))}
            </TabGroup>
          </div>
        </div>
      }
    >
      <Content shelf={shelf} />
    </TabbedPageShell>
  );
}

function GeneralTab({ shelf }: { shelf: string }) {
  return (
    <div className="space-y-10">
      <SettingsSection shelf={shelf} />
      <DeleteSection shelf={shelf} />
    </div>
  );
}

function SettingsSection({ shelf }: { shelf: string }) {
  const { data: info, error } = useShelf(shelf);
  // Keyed on what the server holds, so a change made elsewhere starts the form afresh.
  return info ? <SettingsForm key={`${info.name}:${info.quotaBytes}`} info={info} /> : <LoadError error={error} failure="Could not load the shelf" />;
}

function SettingsForm({ info }: { info: ShelfInfo }) {
  const shelf = info.name;
  const updateShelf = useUpdateShelf(shelf);
  const navigate = useNavigate();
  const initialQuota = formatQuota(info.quotaBytes);
  const [name, setName] = useState(info.name);
  const [quota, setQuota] = useState(initialQuota.amount);
  const [unit, setUnit] = useState<QuotaUnit>(initialQuota.unit);
  const { error, submit } = useSubmit('Could not save');
  // Save shows only while the form differs from what the server holds; a save remounts the form
  // with the new values (see the key above), which hides it again. A quota that does not read as a
  // number counts as a change, so Save stays to say why.
  const changed = name.trim() !== info.name || quotaDiffers(quota, unit, info.quotaBytes);

  function save(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    return submit(() => updateShelf.mutateAsync({ name: name.trim(), quotaBytes: parseQuota(quota, unit) }), (updated) => {
      toast.success('Saved');
      if (updated.name !== shelf) navigate(shelfSettingsRoute(updated.name, 'general'), { replace: true });
    });
  }

  return (
    <section className="space-y-4">
      <SectionTitle description="A new name changes the shelf's links. A quota below what it holds only stops new uploads.">Name and quota</SectionTitle>
      <form onSubmit={(event) => void save(event)} className="space-y-4">
        <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" required />
        <QuotaField amount={quota} unit={unit} onAmountChange={setQuota} onUnitChange={setUnit} error={error} />
        {changed && <Button type="submit" variant="main" loading={updateShelf.isPending}>Save</Button>}
      </form>
    </section>
  );
}

function quotaDiffers(amount: string, unit: QuotaUnit, savedBytes: number | null | undefined): boolean {
  return tryParseQuota(amount, unit) !== (savedBytes ?? null);
}

function GrantsSection({ shelf }: { shelf: string }) {
  const { data: grants = [], error } = useGrants(shelf);
  const setGrant = useSetGrant();
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<ShelfAccessGrant | null>(null);

  return (
    <section className="space-y-4">
      <SectionTitle
        description="Admins reach every shelf; everyone else only the ones they are given."
        actions={<Button size="sm" variant="secondary" icon={<Icon icon={iconMap.add} size={14} />} onClick={() => setAdding(true)}>Give access</Button>}
      >
        Access
      </SectionTitle>
      <LoadError error={error} failure="Could not load who has access" />
      {grants.length > 0 && (
        <CardList>
          {grants.map((grant) => (
            <div key={grant.userId} className="flex flex-wrap items-center gap-3 px-4 py-3">
              <div className="min-w-0 flex-1">
                <div className="truncate text-body-md text-fg">{grant.displayName}</div>
                <div className="truncate text-body-sm text-muted">{grant.email}</div>
              </div>
              <ButtonGroup
                size="sm"
                options={ACCESS_OPTIONS}
                value={grant.permission}
                ariaLabel={`${grant.email}'s access`}
                onChange={(permission) => void runOrToast(() => setGrant.mutateAsync({ shelf, userId: grant.userId, permission }), 'Could not change the access')}
              />
              <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Take ${grant.email}'s access away`} onClick={() => setRemoving(grant)} />
            </div>
          ))}
        </CardList>
      )}
      {adding && <GiveAccessDialog shelf={shelf} grantedUserIds={grants.map((grant) => grant.userId)} onClose={() => setAdding(false)} />}
      {removing && <RemoveGrantDialog shelf={shelf} grant={removing} onClose={() => setRemoving(null)} />}
    </section>
  );
}

function RemoveGrantDialog({ shelf, grant, onClose }: { shelf: string; grant: ShelfAccessGrant; onClose: () => void }) {
  const removeGrant = useRemoveGrant();
  const { error, submit } = useSubmit('Could not take the access away');

  return (
    <FormDialog
      open
      title={`Take ${grant.email}'s access away?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removeGrant.mutateAsync({ shelf, userId: grant.userId }), onClose)}
      submitLabel="Take away"
      submitVariant="danger"
      submitting={removeGrant.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">They no longer see {shelf}. What they stored stays on it.</p>
    </FormDialog>
  );
}

function GiveAccessDialog({ shelf, grantedUserIds, onClose }: { shelf: string; grantedUserIds: string[]; onClose: () => void }) {
  const { data: users, error: usersError } = useUsers();
  const setGrant = useSetGrant();
  const [email, setEmail] = useState('');
  const [permission, setPermission] = useState<ShelfAccessPermission>('read');
  const { error, setError, submit } = useSubmit('Could not give access');

  function give(): Promise<void> {
    if (!users) {
      // Not "no such user": the list to look them up in is not there.
      setError(getErrorMessage(usersError, 'The users have not loaded yet; try again in a moment'));
      return Promise.resolve();
    }

    const user = users.find((candidate) => candidate.email.toLowerCase() === email.trim().toLowerCase());
    if (!user) {
      setError(`There is no user with the email ${email.trim()}`);
      return Promise.resolve();
    }

    if (grantedUserIds.includes(user.id)) {
      setError(`${user.email} has access already; change it in the list`);
      return Promise.resolve();
    }

    return submit(() => setGrant.mutateAsync({ shelf, userId: user.id, permission }), onClose);
  }

  return (
    <FormDialog open title="Give access" onClose={onClose} onSubmit={() => void give()} submitLabel="Give access" submitting={setGrant.isPending} error={error}>
      <Input label="User's email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="off" required />
      <ButtonGroup options={ACCESS_OPTIONS} value={permission} onChange={setPermission} ariaLabel="Access" />
    </FormDialog>
  );
}

function KeysSection({ shelf }: { shelf: string }) {
  const { data: keys = [], error } = useKeys(shelf);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<ShelfKeyInfo | null>(null);

  return (
    <section className="space-y-4">
      <SectionTitle
        description="A key lets a program use this shelf, and only this shelf. Deleting it stops it at once."
        actions={<Button size="sm" variant="secondary" icon={<Icon icon={iconMap.key} size={14} />} onClick={() => setCreating(true)}>New key</Button>}
      >
        Keys
      </SectionTitle>
      <LoadError error={error} failure="Could not load the keys" />
      {keys.length > 0 && (
        <CardList>
          {keys.map((key) => (
            <div key={key.id} className="flex items-center gap-3 px-4 py-3">
              <div className="min-w-0 flex-1">
                <div className="truncate text-body-md text-fg">{key.name}</div>
                <div className="text-body-sm text-muted">{keyPermissionLabel(key.permission)} · created {formatTime(key.createdAt)}</div>
              </div>
              <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Delete the key ${key.name}`} onClick={() => setDeleting(key)} />
            </div>
          ))}
        </CardList>
      )}
      {creating && <CreateKeyDialog shelf={shelf} onClose={() => setCreating(false)} />}
      {deleting && <DeleteKeyDialog shelf={shelf} shelfKey={deleting} onClose={() => setDeleting(null)} />}
    </section>
  );
}

function DeleteKeyDialog({ shelf, shelfKey, onClose }: { shelf: string; shelfKey: ShelfKeyInfo; onClose: () => void }) {
  const deleteKey = useDeleteKey(shelf);
  const { error, submit } = useSubmit('Could not delete the key');

  return (
    <FormDialog
      open
      title={`Delete the key ${shelfKey.name}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => deleteKey.mutateAsync(shelfKey.id), onClose)}
      submitLabel="Delete"
      submitVariant="danger"
      submitting={deleteKey.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">Whatever uses it stops working at once. It cannot be undone.</p>
    </FormDialog>
  );
}

function CreateKeyDialog({ shelf, onClose }: { shelf: string; onClose: () => void }) {
  const createKey = useCreateKey(shelf);
  const [name, setName] = useState('');
  const [permission, setPermission] = useState<ShelfKeyPermission>('addOnly');
  const [created, setCreated] = useState<CreatedShelfKey>();
  const { error, submit } = useSubmit('Could not create the key');

  function create(): Promise<void> {
    return submit(() => createKey.mutateAsync({ name: name.trim(), permission }), setCreated);
  }

  if (created) {
    return (
      <FormDialog open title={`Key "${created.key.name}"`} onClose={onClose}>
        <p className="text-body-md text-fg">Copy the key now: it is shown this once. A program, the byteshelf command line among them, takes these two settings:</p>
        <CopyField label="BYTESHELF_URL" value={`${window.location.origin}/`} copyButtonAriaLabel="Copy the server's address" />
        <CopyField label="BYTESHELF_KEY" value={created.secret} copyButtonAriaLabel="Copy the key" />
      </FormDialog>
    );
  }

  return (
    <FormDialog open title="New key" onClose={onClose} onSubmit={() => void create()} submitLabel="Create" submitting={createKey.isPending} error={error}>
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} placeholder="What uses it, such as backup job" autoComplete="off" required />
      <div className="space-y-1.5">
        <ButtonGroup options={KEY_OPTIONS} value={permission} onChange={setPermission} ariaLabel="What the key may do" className="w-full" />
        <p className="text-body-sm text-muted">Add only stores new files and never replaces or deletes one.</p>
      </div>
    </FormDialog>
  );
}

function DeleteSection({ shelf }: { shelf: string }) {
  const deleteShelf = useDeleteShelf(shelf);
  const navigate = useNavigate();
  const [confirming, setConfirming] = useState(false);
  const { error, setError, submit } = useSubmit('Could not delete the shelf');

  function remove(): Promise<void> {
    return submit(() => deleteShelf.mutateAsync(), () => {
      toast.success(`Deleted ${shelf}`);
      navigate(ROUTES.HOME, { replace: true });
    });
  }

  return (
    <section className="space-y-4">
      <SectionTitle description="Only an empty shelf can be deleted: delete what it holds and empty its trash first.">Delete the shelf</SectionTitle>
      <Button variant="outlineDanger" onClick={() => setConfirming(true)}>Delete shelf</Button>
      <FormDialog
        open={confirming}
        title={`Delete ${shelf}?`}
        onClose={() => {
          setConfirming(false);
          setError(undefined);
        }}
        onSubmit={() => void remove()}
        submitLabel="Delete"
        submitVariant="danger"
        submitting={deleteShelf.isPending}
        error={error}
      >
        <p className="text-body-md text-fg">Its access grants and keys are deleted with it.</p>
      </FormDialog>
    </section>
  );
}
