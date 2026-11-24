// The browser's side of the passkey ceremonies. The server hands options as the JSON
// PublicKeyCredential.parseCreationOptionsFromJSON() and parseRequestOptionsFromJSON() take, and wants
// back what PublicKeyCredential.toJSON() writes. A browser without those three (before Safari 18.4,
// Chrome 129 and Firefox 119) gets no passkey button, rather than conversion code of this page's own.

import { isAbortError } from './abort';

/** Whether this browser can make and use passkeys, with the JSON helpers the ceremonies go through. */
export function passkeysSupported(): boolean {
  const credential = typeof window === 'undefined' ? undefined : window.PublicKeyCredential;
  return credential !== undefined
    && typeof credential.parseCreationOptionsFromJSON === 'function'
    && typeof credential.parseRequestOptionsFromJSON === 'function'
    && typeof credential.prototype.toJSON === 'function';
}

/** Whether this browser can offer saved passkeys as suggestions in a sign-in field. */
export async function passkeyAutofillSupported(): Promise<boolean> {
  if (!passkeysSupported()) return false;
  const check = window.PublicKeyCredential.isConditionalMediationAvailable;
  return typeof check === 'function' ? check.call(window.PublicKeyCredential).catch(() => false) : false;
}

/** Whether a ceremony ended because the person closed the prompt, or the page stopped it, rather than failed. */
export function isCancelled(error: unknown): boolean {
  return isAbortError(error) || (error instanceof DOMException && error.name === 'NotAllowedError');
}
