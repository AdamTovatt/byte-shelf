// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PasswordDialog, type PasswordRequest } from './PasswordDialog';
import { button, fill, press } from '../test/dom';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the password dialog', () => {
  let root: Root;
  let done: ReturnType<typeof vi.fn<(password: string) => void>>;

  async function show(request: PasswordRequest): Promise<void> {
    await act(async () => root.render(<PasswordDialog request={request} onDone={done} onCancel={() => {}} />));
  }

  beforeEach(() => {
    done = vi.fn<(password: string) => void>();
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('stays open saying so when a password to unlock is not the one', async () => {
    await show({ kind: 'unlock', name: 'vault', verify: async (given) => given === 'right' });

    fill('Password', 'wrong');
    await press('Unlock');

    expect(document.body.textContent).toContain('That is not the password.');
    expect(done).not.toHaveBeenCalled();

    fill('Password', 'right');
    await press('Unlock');

    expect(done).toHaveBeenCalledWith('right');
  });

  it('asks again when a new password and its repeat differ, and warns before encrypting', async () => {
    await show({ kind: 'new', name: 'vault' });

    fill('Password', 'one');
    fill('Password again', 'two');
    await press('Next');

    expect(document.body.textContent).toContain('The two passwords differ.');
    expect(button('Encrypt')).toBeNull();

    fill('Password again', 'one');
    await press('Next');
    expect(document.body.textContent).toContain('A lost password cannot be recovered');
    await press('Encrypt');

    expect(done).toHaveBeenCalledWith('one');
  });
});
