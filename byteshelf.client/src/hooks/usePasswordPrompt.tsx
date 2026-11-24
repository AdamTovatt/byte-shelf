import { useCallback, useRef, useState, type ReactNode } from 'react';
import { PasswordDialog, type PasswordRequest } from '../components/PasswordDialog';

interface Pending {
  /** Tells one prompt from the next, even for the same name. */
  id: number;
  request: PasswordRequest;
  resolve: (password: string | null) => void;
}

/** Asks for a password: null when cancelled, as `signal` cancels it, or when another prompt took its place. */
export type AskPassword = (request: PasswordRequest, signal?: AbortSignal) => Promise<string | null>;

/** The one password prompt of a page: `dialog` is what to render of it. */
export function usePasswordPrompt(): { ask: AskPassword; dialog: ReactNode } {
  const [pending, setPending] = useState<Pending | null>(null);
  const prompts = useRef(0);
  // What `pending` is, known at once: a prompt can be asked for before the last one has rendered.
  const open = useRef<Pending | null>(null);

  // Closes only its own prompt: a password check still running when its prompt was cancelled ends
  // after another prompt may have opened, and must leave that one open.
  const settle = useCallback((settled: Pending, password: string | null): void => {
    if (open.current === settled) {
      open.current = null;
      setPending(null);
    }
    settled.resolve(password);
  }, []);

  const ask = useCallback<AskPassword>(
    (request, signal) => new Promise((resolve) => {
      if (signal?.aborted) {
        resolve(null);
        return;
      }
      const asked: Pending = { id: ++prompts.current, request, resolve };
      // The dialog shows one prompt: the one this replaces is answered, or whoever awaits it would for good.
      open.current?.resolve(null);
      open.current = asked;
      setPending(asked);
      signal?.addEventListener('abort', () => settle(asked, null), { once: true });
    }),
    [settle],
  );

  const dialog = pending && (
    <PasswordDialog
      key={pending.id}
      request={pending.request}
      onDone={(password) => settle(pending, password)}
      onCancel={() => settle(pending, null)}
    />
  );
  return { ask, dialog };
}
