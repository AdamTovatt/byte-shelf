// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { filePasswordKey, folderPasswordKey, forgetPasswords, openTabWithoutPasswords, rememberPassword, rememberedPassword } from './passwords';

/** A new tab's copy of this tab's sessionStorage: its items as keys, as a Storage lists them. */
class CopiedStorage {
  [key: string]: unknown;

  constructor(items: Record<string, string>) {
    Object.assign(this, items);
  }

  removeItem(key: string): void {
    delete this[key];
  }
}

describe('kept passwords', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it('keeps a password for the tab under its folder or file', () => {
    rememberPassword(folderPasswordKey('docs', 'folder-1'), 'pw');

    expect(rememberedPassword(folderPasswordKey('docs', 'folder-1'))).toBe('pw');
    expect(rememberedPassword(filePasswordKey('docs', 'folder-1', 'file-1'))).toBe('pw');
    expect(rememberedPassword(filePasswordKey('docs', undefined, 'file-1'))).toBeNull();
    expect(rememberedPassword(folderPasswordKey('photos', 'folder-1'))).toBeNull();
  });

  it('forgets every password on sign-out, and nothing else the tab keeps', () => {
    rememberPassword(folderPasswordKey('docs', 'a'), 'one');
    rememberPassword(folderPasswordKey('photos', 'b'), 'two');
    sessionStorage.setItem('other', 'kept');

    forgetPasswords();

    expect([rememberedPassword(folderPasswordKey('docs', 'a')), rememberedPassword(folderPasswordKey('photos', 'b'))]).toEqual([null, null]);
    expect(sessionStorage.getItem('other')).toBe('kept');
  });

  it('takes the passwords out of the copy of sessionStorage a tab it opens starts with', () => {
    const copied = new CopiedStorage({ [folderPasswordKey('docs', 'a')]: 'one', other: 'kept' });
    vi.stubGlobal('open', () => ({ sessionStorage: copied, close: () => {} }));

    expect(openTabWithoutPasswords()).not.toBeNull();
    expect(Object.keys(copied)).toEqual(['other']);
  });

  it('closes a tab whose copy it cannot reach, rather than leave it holding the passwords', () => {
    const close = vi.fn();
    vi.stubGlobal('open', () => ({ get sessionStorage(): Storage { throw new Error('denied'); }, close }));

    expect(openTabWithoutPasswords()).toBeNull();
    expect(close).toHaveBeenCalled();
  });
});
