import { describe, expect, it } from 'vitest';
import { CHECK_TEXT, WrongPasswordError, checkMatches, createCheck, decryptBlob, decryptBlobWith, encryptBlob, opensWith } from './age';

// What these tests encrypt with: age's default takes about a second a file.
const FAST = 10;

async function text(blob: Blob): Promise<string> {
  return new TextDecoder().decode(await blob.arrayBuffer());
}

describe('age encryption', () => {
  it('decrypts what it encrypts, typed as given', async () => {
    const encrypted = await encryptBlob(new Blob(['räksmörgås']), 'pw', 'text/plain', FAST);

    const decrypted = await decryptBlob(encrypted, 'pw', 'text/plain');

    expect(await text(decrypted)).toBe('räksmörgås');
    expect([encrypted.type, decrypted.type]).toEqual(['text/plain', 'text/plain']);
    expect((await text(encrypted)).startsWith('age-encryption.org/v1\n-> scrypt ')).toBe(true);
  });

  // Slow on purpose (a real work factor 18 scrypt): the one guard on the default.
  it('encrypts at age\'s default work factor unless told otherwise', async () => {
    const encrypted = await encryptBlob(new Blob(['a']), 'pw', 'text/plain');

    expect((await text(encrypted)).split('\n')[1]).toMatch(/ 18$/);
  });

  it('refuses another password as a wrong one', async () => {
    const encrypted = await encryptBlob(new Blob(['a']), 'right', 'text/plain', FAST);

    await expect(decryptBlob(encrypted, 'wrong', 'text/plain')).rejects.toBeInstanceOf(WrongPasswordError);
    expect(await decryptBlobWith(encrypted, 'wrong', 'text/plain')).toBeNull();
    expect(await opensWith(encrypted.stream(), 'right')).toBe(true);
    expect(await opensWith(encrypted.stream(), 'wrong')).toBe(false);
  });

  it('makes a folder check its password matches and no other does', async () => {
    const check = await createCheck('right', FAST);

    expect(await checkMatches(check, 'right')).toBe(true);
    expect(await checkMatches(check, 'wrong')).toBe(false);
    expect(await text(await decryptBlob(new Blob([Uint8Array.from(atob(check), (c) => c.charCodeAt(0))]), 'right', ''))).toBe(CHECK_TEXT);
  });
});
