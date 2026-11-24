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
import { useCreateKey, useGrants, useKeyGrants, useKeys, useRemoveGrant, useRemoveKeyGrant, useSetGrant, useSetKeyGrant } from '../hooks/useAccessQueries';
import { ShelfAccessRows } from '../components/ShelfAccessRows';
import { useDeleteShelf, useShelf, useShelves, useUpdateShelf } from '../hooks/useShelfQueries';
import { useUsers } from '../hooks/useUserQueries';
import { KnownRole } from '../components/KnownRole';
import { useLinks, useRevokeLink } from '../hooks/useLinkQueries';
import type { ShelfLink } from '../services/linkService';
import { formatQuota, formatTime, parseQuota, tryParseQuota, type QuotaUnit } from '../lib/format';
import { ACCESS_OPTIONS, KEY_CHOICES, KEY_OPTIONS, NO_ACCESS, keyPermissionLabel } from '../lib/access';
import { SHELF_SETTINGS_TABS, shelfSettingsRoute, type ShelfSettingsTab } from '../lib/shelfPaths';
import { ROUTES } from '../lib/routes';
import { runOrToast } from '../lib/utils';
import { useSubmit } from '../hooks/useSubmit';
import type { CreatedShelfKey, ShelfAccessGrant, ShelfAccessPermission, ShelfKeyInfo, ShelfPermission } from '../services/accessService';
import type { ShelfInfo } from '../services/shelfService';

/**
 * Each tab: its name, its icon, what it shows, and whether only an admin sees it. Typed by the tab
 * list, so none can be left out.
 */
const TABS: Record<ShelfSettingsTab, { label: string; icon: IconComponent; adminOnly: boolean; Content: (props: { shelf: string }) => ReactNode }> = {
  general: { label: 'General', icon: iconMap.settings, adminOnly: true, Content: GeneralTab },
  access: { label: 'Access', icon: iconMap.users, adminOnly: true, Content: GrantsSection },
  keys: { label: 'Keys', icon: iconMap.key, adminOnly: true, Content: KeysSection },
  links: { label: 'Links', icon: iconMap.link, adminOnly: false, Content: LinksSection },
};

/**
 * One shelf's settings, as tabs that are routes, so each can be linked to and the back button steps
 * between them: for an admin General (name, quota and deleting it), Access (who may use it) and Keys
 * (which programs may); for everyone Links (the share and upload links that have not run out). The tab
 * comes from the route (`SHELF_ROUTE_PATTERNS.settings`); none, or one not shown to this user, goes to
 * the first one shown.
 */
export function ShelfSettingsPage() {
  return <KnownRole>{(isAdmin) => <SettingsTabs isAdmin={isAdmin} />}</KnownRole>;
}

function SettingsTabs({ isAdmin }: { isAdmin: boolean }) {
  const params = useParams();
  const shelf = params.shelf!;
  const shown = SHELF_SETTINGS_TABS.filter((each) => isAdmin || !TABS[each].adminOnly);
  const tab = shown.find((each) => each === params.tab);
  if (!tab) return <Navigate to={shelfSettingsRoute(shelf, shown[0])} replace />;
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
            <TabGroup role="group" aria-label="Settings tabs">
              {shown.map((each) => (
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
  // The Shelves dialog reads the key's shelves by its id, so it stays open while the key leaves this
  // shelf's list; a key's name never changes.
  const [managing, setManaging] = useState<ShelfKeyInfo | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  // Read from the list each render, so the dialog sees the key's shelves as they are, and closes once the key is gone.
  const removing = keys.find((key) => key.id === removingId);

  return (
    <section className="space-y-4">
      <SectionTitle
        description="A key lets a program use the shelves it is given. Taking this shelf from it stops it here at once; a key goes with its last shelf."
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
                <div className="text-body-sm text-muted">{describeKey(key, shelf)} · created {formatTime(key.createdAt)}</div>
              </div>
              <Button size="sm" variant="secondary" aria-label={`The shelves the key ${key.name} works on`} onClick={() => setManaging(key)}>Shelves</Button>
              <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Take the key ${key.name} off ${shelf}`} onClick={() => setRemovingId(key.id)} />
            </div>
          ))}
        </CardList>
      )}
      {creating && <CreateKeyDialog shelf={shelf} onClose={() => setCreating(false)} />}
      {managing && <KeyShelvesDialog shelfKey={managing} onClose={() => setManaging(null)} />}
      {removing && <RemoveKeyDialog shelf={shelf} shelfKey={removing} onClose={() => setRemovingId(null)} />}
    </section>
  );
}

/** What the key may do on this shelf, then the other shelves it works on: "Add only · also on photos (read)". */
function describeKey(key: ShelfKeyInfo, shelf: string): string {
  const here = key.grants.find((grant) => grant.shelfName === shelf);
  const others = key.grants.filter((grant) => grant !== here);
  const also = others.map((grant) => `${grant.shelfName} (${keyPermissionLabel(grant.permission).toLowerCase()})`).join(', ');
  return [here ? keyPermissionLabel(here.permission) : null, others.length > 0 ? `also on ${also}` : null].filter(Boolean).join(' · ');
}

function RemoveKeyDialog({ shelf, shelfKey, onClose }: { shelf: string; shelfKey: ShelfKeyInfo; onClose: () => void }) {
  const removeKeyGrant = useRemoveKeyGrant();
  const { error, submit } = useSubmit('Could not take the key off the shelf');
  const last = shelfKey.grants.length <= 1;

  return (
    <FormDialog
      open
      title={last ? `Delete the key ${shelfKey.name}?` : `Take the key ${shelfKey.name} off ${shelf}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removeKeyGrant.mutateAsync({ shelf, keyId: shelfKey.id }), onClose)}
      submitLabel={last ? 'Delete' : 'Take off'}
      submitVariant="danger"
      submitting={removeKeyGrant.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">
        {last
          ? 'This is its only shelf, so the key is deleted: whatever uses it stops working at once. It cannot be undone.'
          : `Whatever uses it stops working on ${shelf} at once, and keeps working on its other shelves.`}
      </p>
    </FormDialog>
  );
}

/**
 * The key's access on every shelf, each changed as it is picked, as a user's shelves are on the users
 * page. Its only shelf has no "None": that would delete the key in one click, which the bin on its row
 * asks about first.
 */
function KeyShelvesDialog({ shelfKey, onClose }: { shelfKey: ShelfKeyInfo; onClose: () => void }) {
  const { data: shelves = [] } = useShelves();
  const { data: grants = shelfKey.grants, error } = useKeyGrants(shelfKey.id);
  const setKeyGrant = useSetKeyGrant();
  const removeKeyGrant = useRemoveKeyGrant();
  const onlyShelf = grants.length === 1 ? grants[0].shelfId : null;

  return (
    <FormDialog open title={`Shelves for ${shelfKey.name}`} onClose={onClose}>
      <LoadError error={error} failure="Could not load the key's shelves" />
      <ShelfAccessRows<ShelfPermission>
        rows={shelves.map((candidate) => ({
          shelf: candidate.name,
          value: grants.find((grant) => grant.shelfId === candidate.id)?.permission ?? NO_ACCESS,
          options: candidate.id === onlyShelf ? KEY_OPTIONS : KEY_CHOICES,
        }))}
        onSet={(shelf, permission) => setKeyGrant.mutateAsync({ shelf, keyId: shelfKey.id, permission })}
        onRemove={(shelf) => removeKeyGrant.mutateAsync({ shelf, keyId: shelfKey.id })}
        failure={(shelf) => `Could not change the key's access to ${shelf}`}
        ariaLabel={(shelf) => `The key's access to ${shelf}`}
      />
      {onlyShelf && <p className="text-body-sm text-muted">A key keeps at least one shelf; the bin on its row deletes it.</p>}
    </FormDialog>
  );
}

function CreateKeyDialog({ shelf, onClose }: { shelf: string; onClose: () => void }) {
  const createKey = useCreateKey(shelf);
  const [name, setName] = useState('');
  const [permission, setPermission] = useState<ShelfPermission>('addOnly');
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

function LinksSection({ shelf }: { shelf: string }) {
  const { data: links, error } = useLinks(shelf);
  const [revoking, setRevoking] = useState<ShelfLink | null>(null);

  return (
    <section className="space-y-4">
      <SectionTitle description="Share links open a file and upload links store one, without signing in, until they run out. You see the links you made, or every one here if you may change the shelf's files.">
        Links
      </SectionTitle>
      <LoadError error={error} failure="Could not load the links" />
      {links?.length === 0 && <p className="text-body-sm text-muted">No links on this shelf.</p>}
      {links && links.length > 0 && (
        <CardList>
          {links.map((link) => (
            <div key={link.id} className="flex items-center gap-3 px-4 py-3">
              <div className="min-w-0 flex-1">
                <div className="truncate text-body-md text-fg">{linkPlace(link)}</div>
                <div className="text-body-sm text-muted">{linkDetails(link)}</div>
              </div>
              <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Revoke the link to ${linkPlace(link)}`} onClick={() => setRevoking(link)} />
            </div>
          ))}
        </CardList>
      )}
      {revoking && <RevokeLinkDialog shelf={shelf} link={revoking} onClose={() => setRevoking(null)} />}
    </section>
  );
}

/** What a link reaches: a share link's file as it is now, or the place an upload link stores at. */
function linkPlace(link: ShelfLink): string {
  return link.path ?? 'A file in the trash';
}

/** What a link is, who made it and when it runs out. */
function linkDetails(link: ShelfLink): string {
  const kind = link.kind === 'download' ? 'Share link' : link.overwrite ? 'Upload link, replaces a file there' : 'Upload link';
  const maker = link.createdBy.isCaller ? `${link.createdBy.name} (you)` : link.createdBy.name;
  return `${kind} · by ${maker} · until ${formatTime(link.expiresAt)}`;
}

function RevokeLinkDialog({ shelf, link, onClose }: { shelf: string; link: ShelfLink; onClose: () => void }) {
  const revokeLink = useRevokeLink(shelf);
  const { error, submit } = useSubmit('Could not revoke the link');

  return (
    <FormDialog
      open
      title="Revoke this link?"
      onClose={onClose}
      onSubmit={() => void submit(() => revokeLink.mutateAsync(link.id), onClose)}
      submitLabel="Revoke"
      submitVariant="danger"
      submitting={revokeLink.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">Whoever holds the link to {linkPlace(link)} can no longer use it. It cannot be undone.</p>
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
