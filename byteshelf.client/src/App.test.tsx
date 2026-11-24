// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import toast from 'react-hot-toast';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { ThemeProvider } from './ui';
import { decryptBlob } from './lib/encryption/age';
import { leaveStartupToast } from './lib/startupToast';
import { clearTokens } from './services/authService';
import { button, dialogTitles, dropFiles, press, shows, submitPassword, untilFound } from './test/dom';
import { json, stubServer } from './test/fakeServer';
import { signIn } from './test/shelfPage';
import { uploadShelf } from './test/uploadShelf';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let root: Root | null = null;

async function renderApp(url = '/'): Promise<void> {
  history.replaceState(null, '', url);
  const rendered = createRoot(document.body.appendChild(document.createElement('div')));
  root = rendered;
  await act(async () => rendered.render(<ThemeProvider><QueryClientProvider client={new QueryClient()}><App /></QueryClientProvider></ThemeProvider>));
}

afterEach(() => {
  act(() => root?.unmount());
  root = null;
  toast.remove();
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('shows the toast left for it as it starts', async () => {
  stubServer(async () => json({}));
  leaveStartupToast('Paired.');

  await renderApp();

  await shows('Paired.');
});

describe('a file replaced after its page is left', () => {
  let shelf: ReturnType<typeof uploadShelf>;

  /** Drops a file, whose name a file with a password of its own has, on its folder, and leaves for another page. */
  async function dropThenLeave(): Promise<void> {
    signIn();
    shelf = uploadShelf('/a');
    await shelf.put('a.txt', 'own');
    stubServer(async (request) => {
      const { pathname } = new URL(request.url);
      return pathname === '/api/shelves' ? json([]) : shelf.answer(request);
    });
    await renderApp('/shelves/docs/files/a');
    const row = await untilFound('[data-selectable-row]');

    await dropFiles(row, [new File(['new'], 'a.txt')]);
    await shows(`A file already exists at '/a/a.txt'.`);
    await act(async () => document.querySelector<HTMLAnchorElement>('nav a[href="/"]')!.click());
    await vi.waitFor(() => expect(location.pathname).toBe('/'));
    expect(document.querySelector('[data-selectable-row]')).toBeNull();
  }

  it('is encrypted with the password asked for there', async () => {
    await dropThenLeave();

    await press('Replace');
    await shows('Unlock a.txt');
    expect(location.pathname).toBe('/');
    await submitPassword('own');

    await vi.waitFor(() => expect(shelf.sent).toHaveLength(2), { timeout: 10_000 });
    expect(shelf.sent[1]).toMatchObject({ overwrite: true, encrypted: true });
    expect(await (await decryptBlob(shelf.sent[1].contents, 'own', '')).text()).toBe('new');
    await shows('Uploaded a.txt');
    await vi.waitFor(() => expect(document.body.textContent).not.toContain('Uploaded a.txt'), { timeout: 5_000 });
  }, 20_000);

  it('is not asked for once the person is signed out', async () => {
    vi.spyOn(window.location, 'reload').mockImplementation(() => undefined);
    await dropThenLeave();
    await press('Replace');
    await shows('Unlock a.txt');

    await act(async () => {
      clearTokens();
      window.dispatchEvent(new StorageEvent('storage', { key: 'byteshelf_auth_refresh_token' }));
    });

    await vi.waitFor(() => expect(location.pathname).toBe('/login'));
    expect(dialogTitles()).toEqual([]);
    expect(button('Unlock')).toBeNull();
    expect(shelf.sent).toHaveLength(1);
  });
});
