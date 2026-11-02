import { readFileSync } from 'node:fs';
import { Decrypter } from 'age-encryption';
import { describe, expect, it } from 'vitest';

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
});
