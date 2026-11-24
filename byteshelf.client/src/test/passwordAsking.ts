import { vi } from 'vitest';
import type { AskPassword, PasswordRequest } from '../lib/encryption/askPassword';

/** A prompt as the person answers it. */
export interface ShownPrompt {
  request: PasswordRequest;
  /** Gives a password, which closes it unless it is the wrong one. */
  give: (password: string) => Promise<void>;
  cancel: () => void;
}

/** Stands in for the password prompt: the one asked for last, open until it is answered or taken back, or another takes its place. */
export function passwordAsking() {
  /** The one open, if any. */
  const open: ShownPrompt[] = [];

  const ask: AskPassword = (request, signal) => new Promise((resolve) => {
    if (signal?.aborted) {
      resolve(null);
      return;
    }
    function close(password: string | null): void {
      if (open.includes(shown)) open.splice(open.indexOf(shown), 1);
      resolve(password);
    }
    const shown: ShownPrompt = {
      request,
      give: async (password) => {
        if (request.kind === 'new' || await request.verify(password)) close(password);
      },
      cancel: () => close(null),
    };
    open[0]?.cancel();
    open.push(shown);
    signal?.addEventListener('abort', shown.cancel, { once: true });
  });

  /** Starts `operation`, then waits for the prompt it opens. */
  async function untilAsked<T>(operation: () => Promise<T>): Promise<{ answer: Promise<T>; prompt: ShownPrompt }> {
    const answer = operation();
    const prompt = await vi.waitFor(() => {
      const [shown] = open;
      if (!shown) throw new Error('No password was asked for.');
      return shown;
    });
    return { answer, prompt };
  }

  return { ask, open, untilAsked };
}
