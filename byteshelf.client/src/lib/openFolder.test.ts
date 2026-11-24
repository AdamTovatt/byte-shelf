import { describe, expect, it } from 'vitest';
import { shelfDirectory } from '../test/shelfFiles';
import { folderKey, type OpenFolder } from './openFolder';

const vault = { directory: shelfDirectory('/vault'), check: 'check' };
const folder: OpenFolder = { directory: shelfDirectory('/vault/a'), encryptedBy: vault };

describe('an open folder\'s key', () => {
  it('is the same as it is read again, whatever else its record then says', () => {
    expect(folderKey({ directory: { ...folder.directory, encrypted: true }, encryptedBy: { ...vault } })).toBe(folderKey(folder));
  });

  it.each<[string, OpenFolder]>([
    ['another folder at its path', { ...folder, directory: { ...folder.directory, id: 'another' } }],
    ['it at another path', { ...folder, directory: { ...folder.directory, path: '/vault/b' } }],
    ['it unencrypted', { ...folder, encryptedBy: null }],
    ['it encrypted by another folder', { ...folder, encryptedBy: { ...vault, directory: shelfDirectory('/') } }],
    ['it under another password', { ...folder, encryptedBy: { ...vault, check: 'another' } }],
  ])('is not that of %s', (_, other) => {
    expect(folderKey(other)).not.toBe(folderKey(folder));
  });
});
