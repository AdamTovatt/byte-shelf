// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShelfHeader } from './ShelfHeader';
import { saveTokens } from '../services/authService';
import type { ShelfView } from '../lib/shelfPaths';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the shelf header\'s trail', () => {
  let root: Root;
  let container: HTMLDivElement;
  let reactErrors: string[];

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    stubServer(async () => json({ message: 'Not here.' }, 404));
    reactErrors = [];
    vi.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
      reactErrors.push(args.map(String).join(' '));
    });
    container = document.createElement('div');
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function render(shelf: string, view: ShelfView, path?: string): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <ShelfHeader shelf={shelf} view={view} path={path} wrapFolderCrumb={(folder, crumb) => <span data-folder={folder}>{crumb}</span>} />
        </MemoryRouter>
      </QueryClientProvider>,
    ));
  }

  /** Each crumb's words, and the folder it takes drops into, if any. */
  function crumbs(): [string, string | null][] {
    return Array.from(container.querySelectorAll('nav li:not([aria-hidden])'), (item) => [
      item.textContent ?? '',
      item.querySelector('[data-folder]')?.getAttribute('data-folder') ?? null,
    ]);
  }

  it('takes drops into the shelf and its folders only, never on the Shelves crumb, whatever the shelf is called', async () => {
    await render('shelves', 'files', '/trash');

    expect(crumbs()).toEqual([['Shelves', null], ['shelves', '/'], ['trash', '/trash']]);
    expect(reactErrors).toEqual([]);
  });

  it('gives a shelf named like one of its pages a crumb of its own', async () => {
    await render('trash', 'trash');

    expect(crumbs()).toEqual([['Shelves', null], ['trash', null], ['Trash', null]]);
    expect(reactErrors).toEqual([]);
  });
});
