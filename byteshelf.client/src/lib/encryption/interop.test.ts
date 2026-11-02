import { readFileSync } from 'node:fs';
import { Decrypter } from 'age-encryption';
import { describe, expect, it } from 'vitest';
import { checkMatches } from './age';

// dotnet.age is what ByteShelfClient's AgeEncryption (the CLI's age) wrote for 100 KiB of (i % 251)
// with this password at work factor 10. ByteShelfClient.Tests opens the file typage writes for the
// same bytes, so the two are checked to read each other both ways.
describe('age interop', () => {
  it('opens a file ByteShelfClient encrypted', async () => {
    const encrypted = new Uint8Array(readFileSync(new URL('./dotnet.age', import.meta.url)));
    const decrypter = new Decrypter();
    decrypter.addPassphrase('correct horse ☃ räksmörgås');

    const decrypted = await decrypter.decrypt(encrypted);

    expect(decrypted).toEqual(new Uint8Array(100 * 1024).map((_, i) => i % 251));
  });

  // dotnet-check.txt is a folder check ByteShelfClient's FolderPassword made for this password at work
  // factor 10, so a check text that drifts from ByteShelfCommon's FileEncryption.CheckText fails here.
  // ByteShelfClient.Tests matches the check typage makes the same way.
  it('matches a folder check ByteShelfClient made', async () => {
    const check = readFileSync(new URL('./dotnet-check.txt', import.meta.url), 'utf8').trim();

    expect(await checkMatches(check, 'correct horse ☃ räksmörgås')).toBe(true);
    expect(await checkMatches(check, 'another password')).toBe(false);
  });
});
