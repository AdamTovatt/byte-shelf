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
  async function upload(name: string, text: string): Promise<void> {
    await uploader.getByTestId('file-input').setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from(text) });
    await expect(uploader.getByText(`Uploaded ${name}`)).toBeVisible();
    await expect(watcher.getByRole('button', { name, exact: true })).toBeVisible({ timeout: 15_000 });
  }

  await upload('first.txt', 'the first file');
  await upload('hello.txt', 'hello from the browser test');

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
  await owner.getByTestId('file-input').setInputFiles({ name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('for my eyes only') });
  await expect(owner.getByText('Uploaded note.txt')).toBeVisible({ timeout: 15_000 });
  const stored = await owner.evaluate(async () => {
    const response = await fetch('/api/shelves/vault/content?path=/secret/note.txt', { headers: { Authorization: `Bearer ${localStorage.getItem('byteshelf_auth_token')}` } });
    return response.text();
  });
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
