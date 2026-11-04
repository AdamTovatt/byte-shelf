import { Decrypter, Encrypter } from 'age-encryption';

// Password encryption as every ByteShelf client does it: an age file (c2sp.org/age) with a scrypt
// recipient, through typage, so `age -d` and the byteshelf CLI open what the browser writes. The
// password and the plaintext never leave the browser.

/** The scrypt work factor files are encrypted with: age's default, about a second a file. */
export const DEFAULT_WORK_FACTOR = 18;

/** What an encrypted folder's check holds, encrypted with its password (as ByteShelfCommon's FileEncryption.CheckText). */
export const CHECK_TEXT = 'ByteShelf encrypted folder';

/** The password does not open the file; its message is the one people see. */
export class WrongPasswordError extends Error {
  constructor() {
    super('That is not the password.');
    this.name = 'WrongPasswordError';
  }
}

/** typage's message when no password it was given opens a file. */
const NO_MATCH = "no identity matched any of the file's recipients";

/**
 * `plain` as an age file `password` opens, typed `type`. The bytes are gathered into a Blob, which
 * the browser keeps on disk when it is large, since an upload must know its size before it starts.
 * `workFactor` is for tests only, which lower it to run fast.
 */
export async function encryptBlob(plain: Blob, password: string, type: string, workFactor = DEFAULT_WORK_FACTOR): Promise<Blob> {
  return asBlob(await encrypterFor(password, workFactor).encrypt(plain.stream()), type);
}

/** The contents of the age file `encrypted`, typed `type`. */
export async function decryptBlob(encrypted: Blob, password: string, type: string): Promise<Blob> {
  return asBlob(await openStream(encrypted.stream(), password), type);
}

/** As {@link decryptBlob}, or null when `password` does not open it. */
export async function decryptBlobWith(encrypted: Blob, password: string, type: string): Promise<Blob | null> {
  try {
    return await decryptBlob(encrypted, password, type);
  } catch (caught) {
    if (caught instanceof WrongPasswordError) return null;
    throw caught;
  }
}

function encrypterFor(password: string, workFactor: number): Encrypter {
  const encrypter = new Encrypter();
  encrypter.setPassphrase(password);
  encrypter.setScryptWorkFactor(workFactor);
  return encrypter;
}

/** A stream gathered into a Blob, which the browser may keep on disk rather than in memory. */
async function asBlob(stream: ReadableStream<Uint8Array>, type: string): Promise<Blob> {
  return new Blob([await new Response(stream).blob()], { type });
}

/** Whether `password` opens the age file `encrypted`: only its header is read before the answer. */
export async function opensWith(encrypted: ReadableStream<Uint8Array>, password: string): Promise<boolean> {
  try {
    await (await openStream(encrypted, password)).cancel();
    return true;
  } catch (caught) {
    if (caught instanceof WrongPasswordError) return false;
    throw caught;
  }
}

/** An encrypted folder's check for `password`: base64 of an age file of {@link CHECK_TEXT}. */
export async function createCheck(password: string, workFactor = DEFAULT_WORK_FACTOR): Promise<string> {
  const check = await encrypterFor(password, workFactor).encrypt(CHECK_TEXT);
  return btoa(Array.from(check, (byte) => String.fromCharCode(byte)).join(''));
}

/** Whether `password` is the one a folder's `check` was made with. */
export async function checkMatches(check: string, password: string): Promise<boolean> {
  const bytes = Uint8Array.from(atob(check), (character) => character.charCodeAt(0));
  const decrypter = new Decrypter();
  decrypter.addPassphrase(password);
  try {
    return new TextDecoder().decode(await decrypter.decrypt(bytes)) === CHECK_TEXT;
  } catch (caught) {
    if (isNoMatch(caught)) return false;
    throw caught;
  }
}

async function openStream(encrypted: ReadableStream<Uint8Array>, password: string): Promise<ReadableStream<Uint8Array>> {
  const decrypter = new Decrypter();
  decrypter.addPassphrase(password);
  try {
    return await decrypter.decrypt(encrypted);
  } catch (caught) {
    throw isNoMatch(caught) ? new WrongPasswordError() : caught;
  }
}

function isNoMatch(caught: unknown): boolean {
  return caught instanceof Error && caught.message === NO_MATCH;
}
