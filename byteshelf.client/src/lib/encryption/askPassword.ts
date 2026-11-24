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

/**
 * Asks for a password: null when cancelled, as `signal` cancels it, when another prompt took its place, or
 * when the page that asks is left.
 */
export type AskPassword = (request: PasswordRequest, signal?: AbortSignal) => Promise<string | null>;
