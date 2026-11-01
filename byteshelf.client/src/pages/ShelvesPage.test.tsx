// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ShelvesPage } from './ShelvesPage';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the shelves page', () => {
  let root: Root;
  let container: HTMLDivElement;

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    container = document.createElement('div');
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('says the shelves could not be loaded instead of showing none', async () => {
    stubServer(async (request) => new URL(request.url).pathname === '/api/shelves'
      ? json({ message: 'The database is busy.' }, 500)
      : json({ message: 'Not here.' }, 404));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <MemoryRouter><ShelvesPage /></MemoryRouter>
      </QueryClientProvider>,
    ));
    await vi.waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toBe('The database is busy.'));

    expect(container.textContent).not.toContain('access to any shelf');
  });
});
