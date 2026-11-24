// @vitest-environment happy-dom
import { act } from 'react';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../lib/queryKeys';
import { decryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword } from '../lib/encryption/passwords';
import { button, dialogTitles, dropFiles, giveFiles, press, shows, submitPassword, untilFound } from '../test/dom';
import { stubServer } from '../test/fakeServer';
import { renderShelfPage, row, signIn } from '../test/shelfPage';
import { uploadShelf } from '../test/uploadShelf';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../hooks/usePluginHost', () => ({
  usePluginHost: () => ({ plugins: [], failures: new Map() }),
  usePluginsSettled: () => true,
  usePluginsAwaited: () => false,
}));

const FILES = [new File(['new'], 'a.txt'), new File(['new'], 'b.txt')];

describe('uploading into the folder shown', () => {
  let shelf: ReturnType<typeof uploadShelf>;
  let requests: string[];
  let page: Awaited<ReturnType<typeof renderShelfPage>> | null = null;

  beforeEach(() => {
    signIn();
    shelf = uploadShelf('/a');
    requests = stubServer((request) => shelf.answer(request));
  });

  afterEach(() => {
    page?.leave();
    page = null;
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** Opens the folder, and waits until it says `text`. */
  async function open(text = 'Nothing here yet'): Promise<NonNullable<typeof page>> {
    page = await renderShelfPage('/shelves/docs/files/a');
    await shows(text);
    return page;
  }

  /** Drops files on the listing. */
  function drop({ container }: NonNullable<typeof page>): Promise<void> {
    return dropFiles(container.querySelector('nav[aria-label="Breadcrumb"]')!, FILES);
  }

  /** What was looked up to store a file, or sent. */
  function storing(): string[] {
    return requests.filter((line) => line.startsWith('PUT') || line.includes('/directories/encryption'));
  }

  it('stores every file dropped on it, and shows them', async () => {
    await drop(await open());

    await vi.waitFor(async () => {
      await act(async () => {});
      expect([row('a.txt'), row('b.txt')]).not.toContain(null);
    });
    expect(shelf.sent.map(({ path, encrypted }) => [path, encrypted])).toEqual([['/a/a.txt', false], ['/a/b.txt', false]]);
  });

  it('stores every file dropped on an encrypted one with its password, asked for once', async () => {
    await shelf.encrypt('right');
    await drop(await open());
    await shows('Unlock a');
    await submitPassword('right');

    await vi.waitFor(() => expect(shelf.sent.map(({ path, encrypted }) => [path, encrypted])).toEqual([['/a/a.txt', true], ['/a/b.txt', true]]), { timeout: 15_000 });
    expect(dialogTitles()).toEqual([]);
    for (const { contents } of shelf.sent) expect(await (await decryptBlob(contents, 'right', '')).text()).toBe('new');
  }, 30_000);

  it('queues nothing of an encrypted one as its password is not given, asked for once', async () => {
    await shelf.encrypt('right');
    await drop(await open());
    await shows('Unlock a');
    await press('Cancel');
    await act(async () => {});

    expect(dialogTitles()).toEqual([]);
    expect(storing()).toEqual([]);
  });

  it('sends what was picked nowhere once the encryption the folder had as the picker opened is removed', async () => {
    rememberPassword(folderPasswordKey('docs', (await shelf.encrypt('right')).directory.id), 'right');
    const told = vi.spyOn(toast, 'error');
    const { client } = await open();
    await press('Add');
    await press('Upload file');

    shelf.encryptedBy = null;
    await act(() => client.refetchQueries({ queryKey: queryKeys.shelfContents.root }));
    await giveFiles(await untilFound<HTMLInputElement>('[data-testid="file-input"]'), [FILES[0]]);

    await vi.waitFor(() => expect(told).toHaveBeenCalledOnce());
    expect(shelf.sent).toEqual([]);
  });

  it('sends nothing into one that could not be read', async () => {
    const told = vi.spyOn(toast, 'error');
    shelf.fails = ({ url }) => url.includes('/entries');

    await drop(await open('It is down.'));

    expect(told).toHaveBeenCalledWith('Could not upload: this folder has not been read');
    expect(storing()).toEqual([]);
    await press('Add');
    expect(button('Upload file')?.disabled).toBe(true);
  });
});
