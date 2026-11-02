import { useState } from 'react';
import { FormDialog } from './FormDialog';
import { PasswordInput } from './PasswordInput';
import { useSubmit } from '../hooks/useSubmit';
import { WrongPasswordError } from '../lib/encryption/age';

/** What a password is asked for: one to unlock what is encrypted, or a new one to encrypt with. */
export type PasswordRequest =
  | {
      kind: 'unlock';
      /** What it unlocks, such as a folder's or a file's name. */
      name: string;
      /** Whether the password is the right one; a false keeps the dialog open saying so. */
      verify: (password: string) => Promise<boolean>;
    }
  | { kind: 'new'; name: string };

interface PasswordDialogProps {
  request: PasswordRequest;
  /** The password, once it is verified or, for a new one, typed twice and the warning accepted. */
  onDone: (password: string) => void;
  onCancel: () => void;
}

/**
 * Asks for a password. A new one is typed twice, then a second step says what encrypting means
 * before it is used: only the contents are hidden, and a lost password cannot be recovered.
 */
export function PasswordDialog({ request, onDone, onCancel }: PasswordDialogProps) {
  const [password, setPassword] = useState('');
  const [again, setAgain] = useState('');
  const [warned, setWarned] = useState(false);
  const { error, setError, submitting, submit } = useSubmit('Could not check the password');

  if (request.kind === 'unlock') {
    return (
      <FormDialog
        open
        title={`Unlock ${request.name}`}
        onClose={onCancel}
        onSubmit={() => void submit(async () => {
          if (!(await request.verify(password))) throw new WrongPasswordError();
          return password;
        }, onDone)}
        submitLabel="Unlock"
        submitting={submitting}
        submitDisabled={password === ''}
        error={error}
      >
        <PasswordInput label="Password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="off" required />
      </FormDialog>
    );
  }

  if (warned) {
    return (
      <FormDialog open title={`Encrypt ${request.name}?`} onClose={onCancel} onSubmit={() => onDone(password)} submitLabel="Encrypt" submitVariant="danger">
        <p className="text-body-md text-fg">
          A lost password cannot be recovered: nothing, ByteShelf included, can open these contents without it.
        </p>
        <p className="text-body-md text-muted">
          Only the contents are encrypted. Names, sizes, folders and descriptions stay visible, and search still finds them.
        </p>
      </FormDialog>
    );
  }

  return (
    <FormDialog
      open
      title={`Encrypt ${request.name}`}
      onClose={onCancel}
      onSubmit={() => (password === again ? setWarned(true) : setError('The two passwords differ.'))}
      submitLabel="Next"
      submitDisabled={password === '' || again === ''}
      error={error}
    >
      <PasswordInput label="Password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" required />
      <PasswordInput label="Password again" value={again} onChange={(event) => setAgain(event.target.value)} autoComplete="new-password" required />
    </FormDialog>
  );
}
