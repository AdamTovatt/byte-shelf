// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import type { OpenFolder } from '../lib/openFolder';
import { shelfDirectory } from '../test/shelfFiles';
import { useFolderLock, type FolderLock } from './useFolderLock';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the lock of a folder whose password cannot be checked', () => {
  const vault = shelfDirectory('/Vault.album');
  const folder: OpenFolder = { directory: vault, encryptedBy: { directory: vault, check: 'check' } };

  let root: Root;
  let lock: FolderLock;
  let said: MockInstance<typeof toast.error>;
  let fail: (reason: Error) => void;
  const folderPassword = () => new Promise<string | null>((_, reject) => { fail = reject; });

  function Probe({ open }: { open: OpenFolder | null }) {
    ({ lock } = useFolderLock(open, folderPassword));
    return null;
  }

  beforeEach(() => {
    said = vi.spyOn(toast, 'error').mockReturnValue('');
    root = createRoot(document.body.appendChild(document.createElement('div')));
    act(() => root.render(<Probe open={folder} />));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('stays locked, saying why', async () => {
    await act(async () => fail(new Error('It cannot be checked.')));

    expect(said.mock.calls).toEqual([['It cannot be checked.']]);
    expect(lock).toBe('locked');
  });

  it('says nothing once the folder is left', async () => {
    act(() => root.render(<Probe open={null} />));
    await act(async () => fail(new Error('It cannot be checked.')));

    expect(said).not.toHaveBeenCalled();
  });
});
