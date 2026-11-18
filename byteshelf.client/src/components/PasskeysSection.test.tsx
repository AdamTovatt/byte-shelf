// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PasskeysSection } from './PasskeysSection';
import type { PasskeyList } from '../services/passkeyService';
import { saveTokens } from '../services/authService';
import { button, fill, press } from '../test/dom';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const toasts = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('react-hot-toast', () => ({ default: toasts }));

const laptop = { id: 'p1', name: 'Chrome on Mac', createdAt: '2026-11-02T00:00:00Z', lastUsedAt: null };

const creationOptions = { challenge: 'AQID', rp: { id: 'localhost', name: 'ByteShelf' }, user: { id: 'BAU', name: 'ada@example.com', displayName: 'Ada' }, pubKeyCredParams: [] };

describe('the passkeys in settings', () => {
  let root: Root;
  let list: PasskeyList;
  let create: ReturnType<typeof vi.fn>;
  let sent: string[];
  let bodies: Record<string, unknown>;

  /** Shows the section and waits for `text`, which shows once the passkeys are loaded. */
  async function show(text = 'Chrome on Mac'): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<QueryClientProvider client={client}><PasskeysSection /></QueryClientProvider>));
    await vi.waitFor(() => expect(document.body.textContent).toContain(text));
  }

  /** Presses the icon button labelled `label`. */
  async function pressIcon(label: string): Promise<void> {
    const found = document.body.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
    if (!found) throw new Error(`No button labelled ${label}.`);
    await act(async () => found.click());
  }

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    toasts.success.mockReset();
    toasts.error.mockReset();
    list = { available: true, canAdd: true, passkeys: [laptop] };
    bodies = {};
    create = vi.fn().mockResolvedValue({ toJSON: () => ({ id: 'new' }) });
    vi.stubGlobal('PublicKeyCredential', class {
      static parseCreationOptionsFromJSON(json: unknown): unknown {
        return json;
      }

      static parseRequestOptionsFromJSON(json: unknown): unknown {
        return json;
      }

      toJSON(): void {}
    });
    Object.defineProperty(navigator, 'credentials', { value: { create }, configurable: true });
    sent = stubServer(async (request) => {
      const path = new URL(request.url).pathname;
      if (request.method !== 'GET' && request.method !== 'DELETE') bodies[`${request.method} ${path}`] = await request.json();
      if (path === '/api/users/me/passkeys/ceremony') {
        return (bodies[`POST ${path}`] as { currentPassword: string }).currentPassword === 'right'
          ? json({ ceremonyId: 'c1', options: creationOptions })
          : json({ message: 'The current password is incorrect.', code: 'invalid_credentials' }, 401);
      }
      if (path === '/api/users/me/passkeys' && request.method === 'POST') return json({ ...laptop, id: 'p2', name: 'Safari on iPhone' });
      if (request.method === 'GET') return json(list);
      return new Response(null, { status: 204 });
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('lists each passkey with when it was added and last used', async () => {
    await show();

    expect(document.body.textContent).toContain('Chrome on Mac');
    expect(document.body.textContent).toContain('not used yet');
  });

  it('offers no adding where passkeys cannot be added, as on a replica', async () => {
    list = { ...list, canAdd: false };
    await show();

    expect(button('Add passkey')).toBeNull();
  });

  it('says passkeys are off while the server has them off', async () => {
    list = { available: false, canAdd: false, passkeys: [] };

    await show('Passkeys are off on this server');

    expect(button('Add passkey')).toBeNull();
  });

  it('adds a passkey once the password is given, named after this browser', async () => {
    await show();

    await press('Add passkey');
    fill('Current password', 'right');
    await press('Continue');

    await vi.waitFor(() => expect(toasts.success).toHaveBeenCalledWith('Added Safari on iPhone'));
    expect(create).toHaveBeenCalledWith({ publicKey: creationOptions });
    expect(bodies['POST /api/users/me/passkeys']).toMatchObject({ ceremonyId: 'c1', credential: { id: 'new' } });
    expect(button('Continue')).toBeNull();
  });

  it('keeps the dialog open saying so when the password is wrong', async () => {
    await show();

    await press('Add passkey');
    fill('Current password', 'wrong');
    await press('Continue');

    await vi.waitFor(() => expect(document.body.textContent).toContain('The current password is incorrect.'));
    expect(create).not.toHaveBeenCalled();
  });

  it('closes without a word when the browser\'s prompt is closed', async () => {
    create.mockRejectedValue(new DOMException('The prompt was closed.', 'NotAllowedError'));
    await show();

    await press('Add passkey');
    fill('Current password', 'right');
    await press('Continue');

    await vi.waitFor(() => expect(button('Continue')).toBeNull());
    expect(toasts.success).not.toHaveBeenCalled();
    expect(toasts.error).not.toHaveBeenCalled();
    expect(sent).not.toContain('POST /api/users/me/passkeys Bearer t');
  });

  it('renames a passkey', async () => {
    await show();

    await pressIcon('Rename the passkey Chrome on Mac');
    fill('Name', ' Work laptop ');
    await press('Save');

    await vi.waitFor(() => expect(bodies['PUT /api/users/me/passkeys/p1/name']).toEqual({ name: 'Work laptop' }));
  });

  it('removes a passkey once it is confirmed', async () => {
    await show();

    await pressIcon('Remove the passkey Chrome on Mac');
    expect(sent).not.toContain('DELETE /api/users/me/passkeys/p1 Bearer t');
    await press('Remove');

    await vi.waitFor(() => expect(sent).toContain('DELETE /api/users/me/passkeys/p1 Bearer t'));
  });
});
