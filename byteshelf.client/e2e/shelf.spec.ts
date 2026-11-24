import { readFile } from 'node:fs/promises';
import { expect, test, type Browser, type Page } from '@playwright/test';
import { ADMIN_EMAIL, ADMIN_PASSWORD } from './e2eConfig';

// What only a browser shows: signing in, a file uploaded in one tab appearing in another without a
// reload (the server's live updates reaching the query cache), and a download through a link the
// browser follows without a token.

async function signIn(browser: Browser): Promise<Page> {
  const page = await (await browser.newContext({ acceptDownloads: true })).newPage();
  await page.goto('/login');
  await page.getByLabel('Email').fill(ADMIN_EMAIL);
  await page.getByLabel('Password').fill(ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL('/');
  return page;
}

/** Uploads files into the folder shown, through the browser's file picker. */
async function upload(page: Page, files: { name: string; mimeType: string; buffer: Buffer }[]): Promise<void> {
  const picker = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await page.getByRole('button', { name: 'Upload file' }).click();
  await (await picker).setFiles(files);
}

/** What the server holds of a file, as the page's session reads it. */
function storedText(page: Page, url: string): Promise<string> {
  return page.evaluate(async (at) => {
    const response = await fetch(at, { headers: { Authorization: `Bearer ${localStorage.getItem('byteshelf_auth_token')}` } });
    return response.text();
  }, url);
}

test('a file uploaded in one tab appears in another and downloads intact', async ({ browser }) => {
  const uploader = await signIn(browser);
  const watcher = await signIn(browser);

  await uploader.getByRole('button', { name: 'New shelf' }).click();
  await uploader.getByLabel('Name').fill('e2e');
  await uploader.getByRole('button', { name: 'Create' }).click();
  await expect(uploader).toHaveURL('/shelves/e2e');

  await watcher.goto('/shelves/e2e');
  await expect(watcher.getByText('Nothing here yet', { exact: false })).toBeVisible();

  // Creating the shelf makes its contents stale too, and that news may reach the watcher just after
  // the first upload and bring it in. Once the first file shows, no earlier news is left, so only
  // the second upload's own can bring the second file in.
  async function uploadWatched(name: string, text: string): Promise<void> {
    await upload(uploader, [{ name, mimeType: 'text/plain', buffer: Buffer.from(text) }]);
    await expect(uploader.getByText(`Uploaded ${name}`)).toBeVisible();
    await expect(watcher.getByRole('button', { name, exact: true })).toBeVisible({ timeout: 15_000 });
  }

  await uploadWatched('first.txt', 'the first file');
  await uploadWatched('hello.txt', 'hello from the browser test');

  const download = watcher.waitForEvent('download');
  await watcher.getByRole('button', { name: 'Actions for hello.txt' }).click();
  await watcher.getByRole('button', { name: 'Download' }).click();
  const saved = await download;

  expect(saved.suggestedFilename()).toBe('hello.txt');
  expect(await readFile((await saved.path())!, 'utf8')).toBe('hello from the browser test');
});

// What only a browser shows for encryption: typage encrypting there, the password dialogs, and a
// decrypted download from a local blob in a tab that never had the password.
test('a folder encrypted in the browser keeps its files encrypted and opens them with its password', async ({ browser }) => {
  const owner = await signIn(browser);
  await owner.getByRole('button', { name: 'New shelf' }).click();
  await owner.getByLabel('Name').fill('vault');
  await owner.getByRole('button', { name: 'Create' }).click();
  await expect(owner).toHaveURL('/shelves/vault');

  await owner.getByRole('button', { name: 'Add', exact: true }).click();
  await owner.getByRole('button', { name: 'New folder' }).click();
  await owner.getByLabel('Name').fill('secret');
  await owner.getByRole('button', { name: 'Create' }).click();
  await owner.getByRole('button', { name: 'Actions for secret' }).click();
  await owner.getByRole('button', { name: 'Encrypt' }).click();
  await owner.getByLabel('Password', { exact: true }).fill('correct horse');
  await owner.getByLabel('Password again').fill('correct horse');
  await owner.getByRole('button', { name: 'Next' }).click();
  await expect(owner.getByText('A lost password cannot be recovered', { exact: false })).toBeVisible();
  await owner.getByRole('button', { name: 'Encrypt' }).click();
  await expect(owner.getByText('Encrypting secret: done')).toBeVisible({ timeout: 15_000 });

  // This tab unlocked the folder by encrypting it, so the upload asks for nothing.
  await owner.getByRole('button', { name: 'secret', exact: true }).dblclick();
  await expect(owner).toHaveURL('/shelves/vault/files/secret');
  await upload(owner, [{ name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('for my eyes only') }]);
  await expect(owner.getByText('Uploaded note.txt')).toBeVisible({ timeout: 15_000 });
  const stored = await storedText(owner, '/api/shelves/vault/content?path=/secret/note.txt');
  expect(stored.startsWith('age-encryption.org/v1\n')).toBe(true);
  expect(stored).not.toContain('for my eyes only');

  const reader = await signIn(browser);
  await reader.goto('/shelves/vault/files/secret');
  await reader.getByRole('button', { name: 'Actions for note.txt' }).click();
  await reader.getByRole('button', { name: 'Download' }).click();
  await reader.getByLabel('Password', { exact: true }).fill('correct horse');
  const download = reader.waitForEvent('download');
  await reader.getByRole('button', { name: 'Unlock' }).click();
  const saved = await download;

  expect(saved.suggestedFilename()).toBe('note.txt');
  expect(await readFile((await saved.path())!, 'utf8')).toBe('for my eyes only');
});

// What only a browser shows for the preview: an image it decodes itself, loaded with the
// session's cookie, the history and a reload, and a file decrypted in the page to be shown there.
test('a file opens in a preview over the shelf, and an encrypted one once it is unlocked', async ({ browser }) => {
  // Encrypts and decrypts three times, each slow on purpose.
  test.slow();
  const page = await signIn(browser);
  await page.getByRole('button', { name: 'New shelf' }).click();
  await page.getByLabel('Name').fill('preview');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL('/shelves/preview');
  const pixel = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');
  await upload(page, [
    { name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('the note') },
    { name: 'pixel.png', mimeType: 'image/png', buffer: pixel },
    { name: 'todo.txt', mimeType: 'text/plain', buffer: Buffer.from('the last') },
  ]);
  await expect(page.getByText('Uploaded todo.txt')).toBeVisible();

  const preview = page.getByRole('dialog', { name: 'note.txt' });
  await page.getByRole('button', { name: 'note.txt', exact: true }).dblclick();
  await expect(preview.getByText('the note')).toBeVisible();
  await page.keyboard.press('ArrowRight');
  const among = page.getByRole('dialog', { name: 'pixel.png' });
  const image = among.getByRole('img', { name: 'pixel.png' });
  await expect(image).toHaveJSProperty('naturalWidth', 1);

  // Tab goes around within it, past the step buttons only a phone shows.
  await among.getByRole('button', { name: 'Close' }).focus();
  await page.keyboard.press('Tab');
  await expect(among.getByRole('button', { name: 'Previous file' })).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(among.getByRole('button', { name: 'Close' })).toBeFocused();
  const size = page.viewportSize()!;
  await page.setViewportSize({ width: 400, height: 800 });
  await among.getByRole('button', { name: 'Next file' }).click();
  await expect(page.getByRole('dialog', { name: 'todo.txt' }).getByText('the last')).toBeVisible();
  await page.getByRole('button', { name: 'Previous file' }).click();
  await page.setViewportSize(size);
  await page.reload();
  await expect(image).toHaveJSProperty('naturalWidth', 1);
  await page.goBack();
  await expect(page).toHaveURL('/shelves/preview');
  await expect(page.getByRole('dialog')).toHaveCount(0);

  await page.getByRole('button', { name: 'Actions for note.txt' }).click();
  await page.getByRole('button', { name: 'Encrypt' }).click();
  await page.getByLabel('Password', { exact: true }).fill('correct horse');
  await page.getByLabel('Password again').fill('correct horse');
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('button', { name: 'Encrypt' }).click();
  await expect(page.getByText('Encrypting note.txt: done')).toBeVisible({ timeout: 30_000 });

  // A browser that showed the plain contents shows them decrypted from the new ones, never from its cache.
  await page.getByRole('button', { name: 'note.txt', exact: true }).dblclick();
  await expect(preview.getByText('the note')).toBeVisible({ timeout: 30_000 });

  // Another tab never had the password: reached by the arrow keys the file waits to be unlocked.
  const reader = await signIn(browser);
  await reader.goto('/shelves/preview');
  await reader.getByRole('button', { name: 'pixel.png', exact: true }).dblclick();
  await reader.keyboard.press('ArrowLeft');
  const locked = reader.getByRole('dialog', { name: 'note.txt', exact: true });
  await locked.getByRole('button', { name: 'Unlock' }).click();
  await reader.getByLabel('Password', { exact: true }).fill('correct horse');
  await reader.getByRole('dialog', { name: 'Unlock note.txt' }).getByRole('button', { name: 'Unlock' }).click();
  await expect(locked.getByText('the note')).toBeVisible({ timeout: 30_000 });
});

// What only a browser shows for a plugin: its module and stylesheet loaded from the server into the
// signed-in app through the import map, drawing with the app's own React.
test('a Markdown file is previewed by the plugin that takes it', async ({ browser }) => {
  const page = await signIn(browser);
  await page.getByRole('button', { name: 'New shelf' }).click();
  await page.getByLabel('Name').fill('plugins');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL('/shelves/plugins');
  const text = '# Title\n\n| Name | Size |\n| --- | --- |\n| a.txt | 1 |\n\n<b>markup</b> [web](https://elsewhere.test) [near](other.md)\n';
  await upload(page, [{ name: 'notes.md', mimeType: 'text/markdown', buffer: Buffer.from(text) }]);
  await expect(page.getByText('Uploaded notes.md')).toBeVisible();

  await page.getByRole('button', { name: 'notes.md', exact: true }).dblclick();
  const preview = page.getByRole('dialog', { name: 'notes.md' });
  await expect(preview.getByRole('heading', { name: 'Title', level: 1 })).toBeVisible();
  await expect(preview.getByRole('cell', { name: 'a.txt' })).toBeVisible();
  // The plugin's stylesheet, whose rules a table's cells have from nowhere else.
  await expect(preview.getByRole('cell', { name: 'a.txt' })).toHaveCSS('padding-left', '12px');
  await expect(preview.getByRole('link')).toHaveText('web');

  await preview.getByRole('button', { name: 'Source' }).click();
  await expect(preview.getByText('<b>markup</b>', { exact: false })).toBeVisible();
});

// What only a browser shows for a folder type: a plugin's viewer in place of the listing, reading and storing
// through the app's own session, and the history of its contents. Encrypted, its files are decrypted in the
// page, in a tab that never had the password.
test('a folder of a plugin\'s type opens in its viewer, and an encrypted one once it is unlocked', async ({ browser }) => {
  // Encrypts and decrypts, each slow on purpose.
  test.slow();
  const page = await signIn(browser);
  await page.getByRole('button', { name: 'New shelf' }).click();
  await page.getByLabel('Name').fill('folders');
  await page.getByRole('button', { name: 'Create' }).click();
  await expect(page).toHaveURL('/shelves/folders');
  await page.getByRole('button', { name: 'Add', exact: true }).click();
  await page.getByRole('button', { name: 'New folder' }).click();
  await page.getByLabel('Name').fill('Trip.album');
  await page.getByRole('button', { name: 'Create' }).click();

  const folder = page.getByRole('button', { name: 'Trip.album', exact: true });
  await expect(folder.getByTestId('album-icon')).toBeVisible();
  await folder.dblclick();
  await expect(page).toHaveURL('/shelves/folders/files/Trip.album');
  await expect(page.getByRole('heading', { name: 'Album of Trip.album' })).toBeVisible();
  await expect(page.getByLabel('Search files', { exact: false })).toHaveCount(0);
  await page.getByRole('button', { name: 'Add a note' }).click();
  await page.getByRole('button', { name: 'Note 1' }).click();
  await expect(page.getByRole('dialog', { name: 'note-1.txt' }).getByText('Note 1')).toBeVisible();
  await page.goBack();

  await page.getByRole('button', { name: 'Pages of folders' }).click();
  await page.getByRole('button', { name: 'Show contents' }).click();
  await expect(page).toHaveURL('/shelves/folders/files/Trip.album?view=contents');
  await expect(page.getByRole('button', { name: 'note-1.txt', exact: true })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('button', { name: 'Note 1' })).toBeVisible();

  await page.goto('/shelves/folders');
  await page.getByRole('button', { name: 'Actions for Trip.album' }).click();
  await page.getByRole('button', { name: 'Encrypt' }).click();
  await page.getByLabel('Password', { exact: true }).fill('correct horse');
  await page.getByLabel('Password again').fill('correct horse');
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByRole('button', { name: 'Encrypt' }).click();
  await expect(page.getByText('Encrypting Trip.album: done')).toBeVisible({ timeout: 30_000 });

  const reader = await signIn(browser);
  await reader.goto('/shelves/folders/files/Trip.album');
  await reader.getByRole('button', { name: 'Cancel' }).click();
  await expect(reader.getByRole('button', { name: 'note-1.txt', exact: true })).toBeVisible();
  await reader.getByRole('button', { name: 'Unlock' }).click();
  await reader.getByLabel('Password', { exact: true }).fill('correct horse');
  await reader.getByRole('dialog', { name: 'Unlock Trip.album' }).getByRole('button', { name: 'Unlock' }).click();
  await expect(reader.getByRole('button', { name: 'Note 1' })).toBeVisible({ timeout: 30_000 });

  // Stored from the viewer, it is encrypted before it is sent.
  const sent = reader.waitForResponse((response) => response.request().method() === 'PUT' && response.url().includes('/content?'));
  await reader.getByRole('button', { name: 'Add a note' }).click();
  const kept = await (await sent).json() as { id: string; name: string; encrypted: boolean };
  expect(kept).toMatchObject({ name: 'note-2.txt', encrypted: true });
  await expect(reader.getByRole('button', { name: 'Note 2' })).toBeVisible({ timeout: 30_000 });
  expect((await storedText(reader, `/api/shelves/folders/content?id=${kept.id}`)).startsWith('age-encryption.org/v1\n')).toBe(true);
});
