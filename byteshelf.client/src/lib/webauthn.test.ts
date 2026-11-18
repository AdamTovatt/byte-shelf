// @vitest-environment happy-dom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { isCancelled, passkeysSupported } from './webauthn';

describe('webauthn', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('counts a browser with the JSON helpers as supporting passkeys', () => {
    vi.stubGlobal('PublicKeyCredential', class {
      static parseCreationOptionsFromJSON(): void {}
      static parseRequestOptionsFromJSON(): void {}
      toJSON(): void {}
    });

    expect(passkeysSupported()).toBe(true);
  });

  it('counts a browser without them as not supporting passkeys', () => {
    vi.stubGlobal('PublicKeyCredential', class {});

    expect(passkeysSupported()).toBe(false);
  });

  it('tells a closed prompt from a failure', () => {
    expect(isCancelled(new DOMException('closed', 'NotAllowedError'))).toBe(true);
    expect(isCancelled(new DOMException('wrong host', 'SecurityError'))).toBe(false);
  });
});
