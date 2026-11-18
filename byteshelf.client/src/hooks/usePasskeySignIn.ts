import { useCallback, useEffect, useRef, useState } from 'react';
import type { AuthResponse } from '../services/authTypes';
import { beginPasskeySignIn, PasskeySignInError, signInWithPasskey, type SignInCeremony } from '../services/passkeyService';
import { isCancelled, passkeyAutofillSupported, passkeysSupported } from '../lib/webauthn';

interface PasskeySignInHandlers {
  onSignedIn: (tokens: AuthResponse) => void;
  /** A passkey sign-in failed for any reason but the person closing the prompt, which is not a failure. */
  onFailed: () => void;
}

/** How one ceremony ended: signed in, a passkey the person picked refused by the server, or stopped short of the server. */
type Outcome = 'signedIn' | 'refused' | 'stopped';

/**
 * How far into a ceremony's timeout the suggestions ask for a new one. The server lets a ceremony's
 * challenge go once its timeout has passed, while browsers keep suggestions open past it, so a page left
 * open would otherwise refuse the first passkey picked.
 */
const RENEW_AT = 0.9;

/**
 * Signing in with a passkey on the sign-in page: `available` once the server has passkeys on and the
 * browser can use them, `signIn` for the button, and, where the browser offers it, the saved passkeys as
 * suggestions in the email field (conditional mediation) for as long as the page is open.
 */
export function usePasskeySignIn(handlers: PasskeySignInHandlers) {
  const [available, setAvailable] = useState(false);
  const [pending, setPending] = useState(false);
  const suggestions = useRef<AbortController | null>(null);
  const handlersRef = useRef(handlers);
  useEffect(() => {
    handlersRef.current = handlers;
  });

  /**
   * Runs one ceremony to its end. The suggestions run with no one having asked for anything, so only a
   * refusal of a passkey the person picked is a failure there; a browser that refuses the request at
   * once (a page reached at another host than the server's public one) says nothing.
   */
  const run = useCallback(async (ceremony: SignInCeremony, options: { conditional?: boolean; signal?: AbortSignal }): Promise<Outcome> => {
    try {
      handlersRef.current.onSignedIn(await signInWithPasskey(ceremony, options));
      return 'signedIn';
    } catch (caught) {
      if (options.signal?.aborted || isCancelled(caught)) return 'stopped';
      const refused = caught instanceof PasskeySignInError;
      if (refused || !options.conditional) handlersRef.current.onFailed();
      return refused ? 'refused' : 'stopped';
    }
  }, []);

  /**
   * Offers the saved passkeys in the email field, with `begun` or a new ceremony, until one signs in, the
   * browser stops it, or this is started again or the page closes. A new ceremony replaces one that the
   * server refused a passkey for, or that is about to run out.
   */
  const startSuggestions = useCallback(async (begun?: SignInCeremony): Promise<void> => {
    suggestions.current?.abort();
    const stop = new AbortController();
    suggestions.current = stop;
    if (!(await passkeyAutofillSupported())) return;

    let ceremony: SignInCeremony | null = begun ?? null;
    while (!stop.signal.aborted) {
      try {
        ceremony ??= await beginPasskeySignIn();
      } catch {
        return;
      }

      if (!ceremony || stop.signal.aborted) return;

      const round = new AbortController();
      const endRound = (): void => round.abort();
      stop.signal.addEventListener('abort', endRound);
      const renew = setTimeout(endRound, (ceremony.options.timeout ?? 60_000) * RENEW_AT);
      const outcome = await run(ceremony, { conditional: true, signal: round.signal });
      clearTimeout(renew);
      stop.signal.removeEventListener('abort', endRound);

      const renewing = outcome === 'stopped' && round.signal.aborted;
      if (outcome !== 'refused' && !renewing) return;
      ceremony = null;
    }
  }, [run]);

  useEffect(() => {
    if (!passkeysSupported()) return;
    let active = true;
    beginPasskeySignIn()
      .then((ceremony) => {
        if (!active || !ceremony) return;
        setAvailable(true);
        void startSuggestions(ceremony);
      })
      .catch(() => undefined);

    return () => {
      active = false;
      suggestions.current?.abort();
    };
  }, [startSuggestions]);

  const signIn = useCallback(async (): Promise<void> => {
    suggestions.current?.abort();
    setPending(true);
    let outcome: Outcome = 'stopped';
    try {
      const ceremony = await beginPasskeySignIn();
      if (ceremony) outcome = await run(ceremony, {});
    } catch {
      handlersRef.current.onFailed();
    } finally {
      setPending(false);
    }

    if (outcome !== 'signedIn') void startSuggestions();
  }, [run, startSuggestions]);

  return { available, pending, signIn };
}
