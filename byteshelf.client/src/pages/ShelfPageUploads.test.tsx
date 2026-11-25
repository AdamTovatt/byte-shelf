// @vitest-environment happy-dom
import { act, type ReactElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import toast from 'react-hot-toast';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { queryKeys } from '../lib/queryKeys';
import { decryptBlob } from '../lib/encryption/age';
import { folderPasswordKey, rememberPassword } from '../lib/encryption/passwords';
import type { QueryClient } from '@tanstack/react-query';
import { button, dialogTitles, dropFiles, giveFiles, labelled, press, shows, submitPassword, textOf, typeInto, untilFound } from '../test/dom';
import { json, stubServer } from '../test/fakeServer';
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
  /** How the server answers: as the shelf does, unless a test has it otherwise. */
  let serve: (request: Request) => Promise<Response>;

  beforeEach(() => {
    signIn();
    shelf = uploadShelf('/a');
    serve = (request) => shelf.answer(request);
    requests = stubServer((request) => serve(request));
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
  function drop({ container }: NonNullable<typeof page>, files = FILES): Promise<void> {
    return dropFiles(container.querySelector('nav[aria-label="Breadcrumb"]')!, files);
  }

  /** Holds the server's answer to each file sent until what this answers is called. */
  function holdStoring(): () => void {
    let release = (): void => undefined;
    const held = new Promise<void>((resolve) => { release = resolve; });
    serve = async (request) => {
      if (request.method === 'PUT') await held;
      return shelf.answer(request);
    };
    return release;
  }

  /** The names of the listing's rows, in order, an upload's marked with an arrow. */
  function rowNames(): string[] {
    return Array.from(document.body.querySelectorAll('li')).flatMap((item) => {
      const uploading = item.querySelector('[data-upload-row]')?.getAttribute('data-upload-row');
      const file = item.querySelector('[data-selectable-row] button')?.getAttribute('aria-label');
      return uploading ? [`↑${uploading}`] : file ? [file] : [];
    });
  }

  /** Waits until the listing's rows are `names`. */
  function untilRows(...names: string[]): Promise<void> {
    return vi.waitFor(async () => {
      await act(async () => {});
      expect(rowNames()).toEqual(names);
    });
  }

  /** Reads the listing again, and waits until what shows it is told, which is after a timer. */
  async function readListing(client: QueryClient): Promise<void> {
    await act(() => client.refetchQueries({ queryKey: queryKeys.shelfContents.root }));
    await act(() => new Promise((resolve) => setTimeout(resolve)));
  }

  function uploadRowText(name: string): string {
    return document.body.querySelector(`[data-upload-row="${name}"]`)?.textContent ?? '';
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

  it('sends what was picked nowhere once the encryption the folder had as the picker opened is removed, saying so on its row', async () => {
    rememberPassword(folderPasswordKey('docs', (await shelf.encrypt('right')).directory.id), 'right');
    const told = vi.spyOn(toast, 'error');
    const { client } = await open();
    await press('Add');
    await press('Upload file');

    shelf.encryptedBy = null;
    await act(() => client.refetchQueries({ queryKey: queryKeys.shelfContents.root }));
    await giveFiles(await untilFound<HTMLInputElement>('[data-testid="file-input"]'), [FILES[0]]);

    await shows('a is no longer encrypted as it was, or where it was.');
    expect(rowNames()).toEqual(['↑a.txt']);
    expect(told).not.toHaveBeenCalled();
    expect(shelf.sent).toEqual([]);
  });

  it('shows each file dropped as a row at once, among the files by name, its stage on it, until it is the file stored', async () => {
    await shelf.put('aa.txt');
    const release = holdStoring();

    await drop(await open('aa.txt'));

    await untilRows('↑a.txt', 'aa.txt', '↑b.txt');
    expect(uploadRowText('a.txt')).toContain('Finishing');
    expect(uploadRowText('b.txt')).toContain('Waiting');
    expect(document.body.querySelector('[data-upload-row="a.txt"] [role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('100');
    // Once all is sent, Cancel could no longer stop it.
    expect(labelled('Cancel uploading a.txt')?.classList.contains('invisible')).toBe(true);
    expect(labelled('Cancel uploading b.txt')?.classList.contains('invisible')).toBe(false);
    release();
    await untilRows('a.txt', 'aa.txt', 'b.txt');
  });

  it('keeps a stored file\'s row until the listing is read again, and then leaves it to the listing', async () => {
    let listingRead: Promise<void> | null = null;
    let readThem = (): void => undefined;
    let gone = false;
    serve = async (request) => {
      if (!new URL(request.url).pathname.endsWith('/entries')) return shelf.answer(request);
      if (listingRead) await listingRead;
      const listing = (await (await shelf.answer(request)).json()) as { files: { id: string }[] };
      return json({ ...listing, files: listing.files.filter(({ id }) => !gone || !shelf.isSent(id)) });
    };
    const { client } = await open();
    listingRead = new Promise((resolve) => { readThem = resolve; });

    await drop(page!);
    await untilRows('a.txt', 'b.txt');
    readThem();
    await readListing(client);
    expect(rowNames()).toEqual(['a.txt', 'b.txt']);
    // Left to the listing, they go as it no longer has them, as once they are deleted elsewhere.
    gone = true;
    await readListing(client);
    expect(rowNames()).toEqual([]);
  });

  it('lets go of a stored file\'s row once the listing is read again without it, as once it is deleted before that', async () => {
    serve = async (request) => {
      const answered = await shelf.answer(request);
      if (!new URL(request.url).pathname.endsWith('/entries')) return answered;
      const listing = (await answered.json()) as { files: { id: string }[] };
      return json({ ...listing, files: listing.files.filter(({ id }) => !shelf.isSent(id)) });
    };
    const { client } = await open();

    await drop(page!, [FILES[0]]);
    await vi.waitFor(() => expect(shelf.sent).toHaveLength(1));
    await readListing(client);

    expect(rowNames()).toEqual([]);
  });

  it('shows the rows of a folder whose path came decomposed in the address, as the server has it composed', async () => {
    shelf = uploadShelf('/café');
    const release = holdStoring();
    page = await renderShelfPage('/shelves/docs/files/café');
    await shows('Nothing here yet');

    await drop(page, [FILES[0]]);

    await untilRows('↑a.txt');
    release();
    await untilRows('a.txt');
  });

  it('cancels an upload from its row at once, sending nothing of it', async () => {
    const release = holdStoring();
    await drop(await open());
    await untilRows('↑a.txt', '↑b.txt');

    await act(async () => labelled('Cancel uploading b.txt')!.click());

    expect(rowNames()).toEqual(['↑a.txt']);
    release();
    await untilRows('a.txt');
    expect(shelf.sent.map(({ path }) => path)).toEqual(['/a/a.txt']);
  });

  it('offers a taken name for replacing on its row, and the replacement takes the place of the file there', async () => {
    const told = vi.spyOn(toast, 'error');
    await shelf.put('a.txt');
    await drop(await open('a.txt'), [FILES[0]]);
    await shows('A file with this name is already here.');
    expect(rowNames()).toEqual(['a.txt', '↑a.txt']);
    const release = holdStoring();

    await press('Replace');

    await untilRows('↑a.txt');
    release();
    await untilRows('a.txt');
    expect(shelf.sent.at(-1)).toMatchObject({ path: '/a/a.txt', overwrite: true });
    expect(told).not.toHaveBeenCalled();
  });

  it('says on its row why replacing a file failed, and offers it again', async () => {
    await shelf.put('a.txt');
    await drop(await open('a.txt'), [FILES[0]]);
    await shows('A file with this name is already here.');
    shelf.fails = ({ method }) => method === 'PUT';

    await press('Replace');

    await shows('Could not replace the file here: It is down.');
    expect(rowNames()).toEqual(['a.txt', '↑a.txt']);
    expect(button('Replace')).not.toBeNull();
  });

  it('says why one failed on its row, until it is dismissed', async () => {
    shelf.fails = ({ method }) => method === 'PUT';
    await drop(await open(), [FILES[0]]);
    await shows('It is down.');

    await press('Dismiss');

    expect(rowNames()).toEqual([]);
    await shows('Nothing here yet');
  });

  it('tells of a failure in a toast while a search hides its row', async () => {
    const told = vi.spyOn(toast, 'error');
    shelf.fails = ({ method }) => method === 'PUT';
    const release = holdStoring();
    const { container } = await open();
    await drop(page!, [FILES[0]]);
    await untilRows('↑a.txt');

    typeInto(container.querySelector<HTMLInputElement>('input[aria-label="Search files in this folder and below"]')!, 'a');
    release();

    await vi.waitFor(() => expect(told).toHaveBeenCalledOnce());
    expect(textOf(told.mock.calls[0][0] as ReactElement)).toBe('Could not upload a.txt:It is down.');
  });

  it('tells of a name taken in a folder left in a toast that only says so, and offers replacing it on its row there', async () => {
    const told = vi.spyOn(toast, 'error');
    await shelf.put('a.txt');
    const release = holdStoring();
    await drop(await open('a.txt'), [FILES[0]]);
    await untilRows('a.txt', '↑a.txt');

    await page!.page.to('/shelves/docs/files/b');
    release();

    await vi.waitFor(() => expect(told).toHaveBeenCalledOnce());
    const message = told.mock.calls[0][0] as ReactElement;
    expect(textOf(message)).toBe('Could not upload a.txt:A file with this name is already in /a.');
    expect(renderToStaticMarkup(message)).not.toContain('<button');
    await page!.page.back();
    await shows('A file with this name is already here.');
    expect(button('Replace')).not.toBeNull();
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
