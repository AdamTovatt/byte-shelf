import { apiClient, unwrapNoContentResponse, unwrapResponse } from './apiClient';
import { AUTH_PATHS } from './authPaths';
import type { AuthResponse } from './authTypes';
import type { components } from '../types/api';
import { deviceName } from '../lib/deviceName';

export type PasskeyInfo = components['schemas']['PasskeyInfo'];
export type PasskeyList = components['schemas']['PasskeyList'];

/** A passkey ceremony begun: the options for the browser and the id its answer goes back with. */
export interface Ceremony<TOptions> {
  ceremonyId: string;
  options: TOptions;
}

export type SignInCeremony = Ceremony<PublicKeyCredentialRequestOptionsJSON>;

/**
 * Thrown when the server would not sign in with the passkey the browser answered with, whatever the
 * reason: the sign-in page shows one line for every passkey failure but a closed prompt.
 */
export class PasskeySignInError extends Error {
  constructor() {
    super('The passkey could not sign you in');
  }
}

/**
 * Begins a passkey sign-in; null when the server has passkeys off. Like the other sign-in requests it
 * runs without a session, so it goes around the typed apiClient.
 */
export async function beginPasskeySignIn(): Promise<SignInCeremony | null> {
  const response = await fetch(AUTH_PATHS.PASSKEY_CEREMONY, { method: 'POST' });
  if (response.status === 404) return null;
  if (!response.ok) throw new PasskeySignInError();
  return (await response.json()) as SignInCeremony;
}

/**
 * Runs a ceremony begun with `beginPasskeySignIn` to its end: the browser's prompt, or with `conditional`
 * its suggestions in the email field, then the server's check. Throws the browser's error when the
 * prompt was closed or the page stopped it (`signal`), and `PasskeySignInError` when the server refused.
 */
export async function signInWithPasskey(ceremony: SignInCeremony, options: { conditional?: boolean; signal?: AbortSignal } = {}): Promise<AuthResponse> {
  const credential = (await navigator.credentials.get({
    publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(ceremony.options),
    mediation: options.conditional ? 'conditional' : undefined,
    signal: options.signal,
  })) as PublicKeyCredential | null;
  if (!credential) throw new PasskeySignInError();

  const response = await fetch(AUTH_PATHS.PASSKEY, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ceremonyId: ceremony.ceremonyId, credential: credential.toJSON() }),
  });

  // A refusal does not tell the browser to forget the passkey (PublicKeyCredential.signalUnknownCredential):
  // the server answers the same 401 for a passkey it does not know and for one that failed a check, and a
  // browser told to forget a passkey that works would lose it for good.
  if (!response.ok) throw new PasskeySignInError();
  return (await response.json()) as AuthResponse;
}

export function fetchPasskeys(): Promise<PasskeyList> {
  return unwrapResponse(apiClient.GET('/api/users/me/passkeys'));
}

/**
 * Adds a passkey to the signed-in user, who gives their password for it: the browser's prompt makes it,
 * and it is named after this browser and device. Throws the browser's error when the prompt was closed.
 */
export async function addPasskey(currentPassword: string): Promise<PasskeyInfo> {
  const ceremony = (await unwrapResponse(apiClient.POST('/api/users/me/passkeys/ceremony', { body: { currentPassword } }))) as Ceremony<PublicKeyCredentialCreationOptionsJSON>;
  const credential = (await navigator.credentials.create({
    publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(ceremony.options),
  })) as PublicKeyCredential | null;
  if (!credential) throw new Error('The browser made no passkey');

  return unwrapResponse(apiClient.POST('/api/users/me/passkeys', {
    body: { ceremonyId: ceremony.ceremonyId, credential: credential.toJSON(), name: deviceName(navigator.userAgent) },
  }));
}

export function renamePasskey(passkeyId: string, name: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.PUT('/api/users/me/passkeys/{passkeyId}/name', { params: { path: { passkeyId } }, body: { name } }));
}

export function removePasskey(passkeyId: string): Promise<void> {
  return unwrapNoContentResponse(apiClient.DELETE('/api/users/me/passkeys/{passkeyId}', { params: { path: { passkeyId } } }));
}
