// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DialogHost } from '../contexts/DialogHost';
import { UploadHost, useUploadFiles } from '../contexts/UploadHost';
import { folderPasswordKey, rememberPassword } from '../lib/encryption/passwords';
import type { OpenFolder } from '../lib/openFolder';
import { dialogTitles, press, shows } from '../test/dom';
import { stubServer } from '../test/fakeServer';
import { signIn } from '../test/shelfPage';
import { uploadShelf } from '../test/uploadShelf';
import type { UploadFiles } from './useUploadQueue';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const FILES = [new File(['new'], 'a.txt')];

describe('the uploads of the app', () => {
  let root: Root;
  let shelf: ReturnType<typeof uploadShelf>;
  let uploadFiles: UploadFiles;

  function Page() {
    uploadFiles = useUploadFiles();
    return null;
  }

  async function render(uploads: boolean): Promise<void> {
    await act(async () => root.render(<QueryClientProvider client={client}><DialogHost>{uploads && <UploadHost><Page /></UploadHost>}</DialogHost></QueryClientProvider>));
  }

  let client: QueryClient;

  beforeEach(async () => {
    client = new QueryClient();
    signIn();
    shelf = uploadShelf('/a');
    stubServer((request) => shelf.answer(request));
    root = createRoot(document.body.appendChild(document.createElement('div')));
    await render(true);
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** Uploads into /a, which is encrypted but was read as it was not, up to its prompt. */
  async function untilAskedInTurn(): Promise<void> {
    const read: OpenFolder = { directory: shelf.directory, encryptedBy: null };
    await shelf.encrypt('right');
    await act(async () => uploadFiles('docs', read, FILES));
    await shows('Unlock a');
  }

  it('ask for a folder\'s password first beside the one asked for in turn, neither cancelling the other', async () => {
    await untilAskedInTurn();
    const other = { ...shelf.directory, id: 'b', name: 'b', path: '/b' };

    await act(async () => uploadFiles('docs', { directory: other, encryptedBy: { directory: other, check: shelf.encryptedBy!.check } }, FILES));

    await shows('Unlock b');
    expect(dialogTitles()).toEqual(['Unlock a', 'Unlock b']);
    await press('Cancel');
    expect(dialogTitles()).toHaveLength(1);
  });

  it('say so as a folder\'s password could not be checked', async () => {
    const told = vi.spyOn(toast, 'error');
    const encryptedBy = { directory: shelf.directory, check: 'not base64!' };
    rememberPassword(folderPasswordKey('docs', shelf.directory.id), 'right');

    await act(async () => uploadFiles('docs', { directory: shelf.directory, encryptedBy }, FILES));

    expect(told).toHaveBeenCalledOnce();
    expect(shelf.sent).toEqual([]);
  });

  it('ask no longer, and send nothing, once they are gone', async () => {
    await untilAskedInTurn();

    await render(false);

    expect(dialogTitles()).toEqual([]);
    expect(shelf.sent).toEqual([]);
  });
});
