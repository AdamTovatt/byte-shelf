// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useUploads } from './useUploads';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const storedFile = { id: 'f', path: '/a.txt', name: 'a.txt', sizeBytes: 1, contentType: 'text/plain' };

describe('the upload list', () => {
  let root: Root;
  let uploads: ReturnType<typeof useUploads>;

  function Probe() {
    uploads = useUploads('docs');
    return null;
  }

  beforeEach(async () => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    root = createRoot(document.createElement('div'));
    await act(async () => root.render(<QueryClientProvider client={new QueryClient()}><Probe /></QueryClientProvider>));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('holds a file whose name is taken until it is replaced, then sends it with overwrite', async () => {
    const sent = stubServer(async (request) =>
      new URL(request.url).searchParams.get('overwrite') === 'true'
        ? json(storedFile)
        : json({ message: 'There is already a file at /a.txt.', code: 'conflict' }, 409));

    await act(async () => uploads.upload([new File(['a'], 'a.txt')], '/'));
    await vi.waitFor(() => expect(uploads.items[0]?.status).toBe('taken'));
    await act(async () => uploads.replace(uploads.items[0]));
    await vi.waitFor(() => expect(uploads.items[0]?.status).toBe('done'));

    expect(sent.map((line) => line.split(' Bearer')[0])).toEqual([
      'PUT /api/shelves/docs/content?path=%2Fa.txt&overwrite=false',
      'PUT /api/shelves/docs/content?path=%2Fa.txt&overwrite=true',
    ]);
  });

  it('marks any other refusal as failed', async () => {
    stubServer(async () => json({ message: 'The shelf is full.', code: 'quota_exceeded' }, 507));

    await act(async () => uploads.upload([new File(['a'], 'a.txt')], '/'));
    await vi.waitFor(() => expect(uploads.items[0]?.status).toBe('failed'));

    expect(uploads.items[0].error).toBe('The shelf is full.');
  });
});
