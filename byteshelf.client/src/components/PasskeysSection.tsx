import { useState } from 'react';
import toast from 'react-hot-toast';
import { Button, Icon, IconButton, Input, iconMap } from '../ui';
import { CardList } from './CardList';
import { FormDialog } from './FormDialog';
import { LoadError } from './LoadError';
import { PasswordInput } from './PasswordInput';
import { SectionTitle } from './SectionTitle';
import { useAddPasskey, usePasskeys, useRemovePasskey, useRenamePasskey } from '../hooks/useUserQueries';
import { useSubmit } from '../hooks/useSubmit';
import { formatDate, formatTime } from '../lib/format';
import { isCancelled, passkeysSupported } from '../lib/webauthn';
import type { PasskeyInfo } from '../services/passkeyService';

/** The signed-in user's passkeys in their settings: one line each, adding one, renaming and removing. */
export function PasskeysSection() {
  const { data, error } = usePasskeys();
  const [adding, setAdding] = useState(false);
  const [renaming, setRenaming] = useState<PasskeyInfo | null>(null);
  const [removing, setRemoving] = useState<PasskeyInfo | null>(null);
  const canAdd = data?.canAdd === true && passkeysSupported();

  return (
    <section className="space-y-4">
      <SectionTitle
        description={data?.available === false
          ? 'Passkeys are off on this server: it has no public address set.'
          : 'Sign in with your fingerprint, face or screen lock instead of your password, which keeps working too.'}
        actions={canAdd && (
          <Button size="sm" variant="secondary" icon={<Icon icon={iconMap.key} size={14} />} onClick={() => setAdding(true)}>
            Add passkey
          </Button>
        )}
      >
        Passkeys
      </SectionTitle>
      <LoadError error={error} failure="Could not load the passkeys" />
      {data && data.passkeys.length > 0 && (
        <CardList>
          {data.passkeys.map((passkey) => (
            <div key={passkey.id} className="flex items-center gap-3 px-4 py-3">
              <div className="min-w-0 flex-1">
                <div className="truncate text-body-md text-fg">{passkey.name}</div>
                <div className="text-body-sm text-muted">
                  Added {formatDate(passkey.createdAt)} · {passkey.lastUsedAt ? `last used ${formatTime(passkey.lastUsedAt)}` : 'not used yet'}
                </div>
              </div>
              <IconButton variant="ghost" size="sm" icon={iconMap.rename} aria-label={`Rename the passkey ${passkey.name}`} onClick={() => setRenaming(passkey)} />
              <IconButton variant="ghost" size="sm" icon={iconMap.delete} aria-label={`Remove the passkey ${passkey.name}`} onClick={() => setRemoving(passkey)} />
            </div>
          ))}
        </CardList>
      )}
      {adding && <AddPasskeyDialog onClose={() => setAdding(false)} />}
      {renaming && <RenamePasskeyDialog passkey={renaming} onClose={() => setRenaming(null)} />}
      {removing && <RemovePasskeyDialog passkey={removing} onClose={() => setRemoving(null)} />}
    </section>
  );
}

/** Asks for the password, which adding a passkey takes, then runs the browser's prompt. */
function AddPasskeyDialog({ onClose }: { onClose: () => void }) {
  const addPasskey = useAddPasskey();
  const [password, setPassword] = useState('');
  const { error, setError, submit } = useSubmit('Could not add the passkey');

  function add(): Promise<void> {
    return submit(
      async () => {
        try {
          return await addPasskey.mutateAsync(password);
        } catch (caught) {
          // Closing the browser's prompt is the person changing their mind, not a failure.
          if (isCancelled(caught)) return null;
          throw caught;
        }
      },
      (added) => {
        if (added) toast.success(`Added ${added.name}`);
        onClose();
      },
    );
  }

  return (
    <FormDialog open title="Add passkey" onClose={onClose} onSubmit={() => void add()} submitLabel="Continue" submitting={addPasskey.isPending} error={error}>
      <p className="text-body-md text-fg">Your browser then asks you to make the passkey.</p>
      <PasswordInput
        label="Current password"
        value={password}
        onChange={(event) => {
          setPassword(event.target.value);
          setError(undefined);
        }}
        autoComplete="current-password"
        required
      />
    </FormDialog>
  );
}

function RenamePasskeyDialog({ passkey, onClose }: { passkey: PasskeyInfo; onClose: () => void }) {
  const renamePasskey = useRenamePasskey();
  const [name, setName] = useState(passkey.name);
  const { error, submit } = useSubmit('Could not rename the passkey');

  return (
    <FormDialog
      open
      title="Rename passkey"
      onClose={onClose}
      onSubmit={() => void submit(() => renamePasskey.mutateAsync({ passkeyId: passkey.id, name: name.trim() }), onClose)}
      submitting={renamePasskey.isPending}
      error={error}
    >
      <Input label="Name" value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" required />
    </FormDialog>
  );
}

function RemovePasskeyDialog({ passkey, onClose }: { passkey: PasskeyInfo; onClose: () => void }) {
  const removePasskey = useRemovePasskey();
  const { error, submit } = useSubmit('Could not remove the passkey');

  return (
    <FormDialog
      open
      title={`Remove the passkey ${passkey.name}?`}
      onClose={onClose}
      onSubmit={() => void submit(() => removePasskey.mutateAsync(passkey.id), onClose)}
      submitLabel="Remove"
      submitVariant="danger"
      submitting={removePasskey.isPending}
      error={error}
    >
      <p className="text-body-md text-fg">It stops signing you in, and wherever you are signed in with it is signed out at once.</p>
    </FormDialog>
  );
}
