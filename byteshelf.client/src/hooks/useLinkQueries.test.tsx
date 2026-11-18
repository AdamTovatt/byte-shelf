// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useCreateDownloadLink, useLinks } from './useLinkQueries';
import { saveTokens } from '../services/authService';
import type { ShelfLink } from '../services/linkService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const SHARE_LINK: ShelfLink = {
  id: 'l1',
  kind: 'download',
  path: '/a.txt',
  fileId: 'f',
  overwrite: false,
  description: null,
  createdBy: { kind: 'user', id: 'u', name: 'Ada', isCaller: true },
  createdAt: '2026-11-02T00:00:00Z',
  expiresAt: '2026-11-03T00:00:00Z',
};

describe('making a share link', () => {
  let root: Root;
  let links: ReturnType<typeof useLinks>;
  let create: ReturnType<typeof useCreateDownloadLink>;

  function Probe() {
    links = useLinks('docs');
    create = useCreateDownloadLink('docs');
    return null;
  }

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('refreshes the shelf\'s links, so the new one is listed', async () => {
    let made: ShelfLink[] = [];
    stubServer(async (request) => {
      if (request.method === 'POST') {
        made = [SHARE_LINK];
        return json({ id: 'l1', url: '/api/downloads/x', expiresAt: SHARE_LINK.expiresAt });
      }
      return json(made);
    });
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><Probe /></QueryClientProvider>));
    await vi.waitFor(() => expect(links.data).toEqual([]));

    await act(async () => void (await create.mutateAsync('f')));

    await vi.waitFor(() => expect(links.data).toEqual([SHARE_LINK]));
  });
});
