import { useCallback, useRef } from 'react';
import { PasswordDialog, type PasswordRequest } from '../components/PasswordDialog';
import { useShowDialog } from '../contexts/DialogHost';

/**
 * Asks for a password: null when cancelled, as `signal` cancels it, when another prompt took its place, or
 * when the page that asks is left.
 */
export type AskPassword = (request: PasswordRequest, signal?: AbortSignal) => Promise<string | null>;

/** The one password prompt of a page. */
export function usePasswordPrompt(): AskPassword {
  const show = useShowDialog();
  /** Takes back the prompt shown, if any. */
  const takeBack = useRef<() => void>(() => undefined);

  return useCallback<AskPassword>(
    (request, signal) => new Promise((resolve) => {
      if (signal?.aborted) {
        resolve(null);
        return;
      }
      // One prompt shows: the one this replaces is answered, or whoever awaits it would for good.
      takeBack.current();

      // Set as it closes, and not by a password check still running when its prompt was cancelled.
      let given: string | null = null;
      const dialog = show((close) => (
        <PasswordDialog request={request} onDone={(password) => { given = password; close(); }} onCancel={close} />
      ));
      takeBack.current = dialog.close;
      signal?.addEventListener('abort', dialog.close, { once: true });
      void dialog.closed.then(() => resolve(given));
    }),
    [show],
  );
}
