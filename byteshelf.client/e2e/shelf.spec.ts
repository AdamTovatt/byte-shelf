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
  await expect(watcher.getByText('Nothing here yet')).toBeVisible();

  // Creating the shelf makes its contents stale too, and that news may reach the watcher just after
  // the first upload and bring it in. Once the first file shows, no earlier news is left, so only
  // the second upload's own can bring the second file in.
  async function upload(name: string, text: string, stored: number): Promise<void> {
    await uploader.getByTestId('file-input').setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from(text) });
    await expect(uploader.getByText('Stored')).toHaveCount(stored);
    await expect(watcher.getByRole('button', { name, exact: true })).toBeVisible({ timeout: 15_000 });
  }

  await upload('first.txt', 'the first file', 1);
  await upload('hello.txt', 'hello from the browser test', 2);

  const download = watcher.waitForEvent('download');
  await watcher.getByRole('button', { name: 'Actions for hello.txt' }).click();
  await watcher.getByRole('button', { name: 'Download' }).click();
  const saved = await download;

  expect(saved.suggestedFilename()).toBe('hello.txt');
  expect(await readFile((await saved.path())!, 'utf8')).toBe('hello from the browser test');
});
