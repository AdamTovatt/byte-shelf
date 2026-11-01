// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useUploads, type UploadState } from './useUploads';
import { saveTokens } from '../services/authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const storedFile = { id: 'f', path: '/a.txt', name: 'a.txt', sizeBytes: 1, contentType: 'text/plain' };

describe('uploads', () => {
  let root: Root;
  let uploads: ReturnType<typeof useUploads>;
  let states: UploadState[];
  const last = () => states[states.length - 1];

  function Probe() {
    uploads = useUploads('docs', (_upload, state) => states.push(state));
    return null;
  }

  beforeEach(async () => {
    states = [];
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

  it('offers a file whose name is taken for replacing, then sends it with overwrite', async () => {
    const sent = stubServer(async (request) =>
      new URL(request.url).searchParams.get('overwrite') === 'true'
        ? json(storedFile)
        : json({ message: 'There is already a file at /a.txt.', code: 'conflict' }, 409));

    await act(async () => uploads.upload([new File(['a'], 'a.txt')], '/'));
    await vi.waitFor(() => expect(last()?.status).toBe('taken'));
    const taken = last();
    if (taken.status !== 'taken') throw new Error('unreachable');
    await act(async () => taken.replace());
    await vi.waitFor(() => expect(last().status).toBe('done'));

    expect(sent.map((line) => line.split(' Bearer')[0])).toEqual([
      'PUT /api/shelves/docs/content?path=%2Fa.txt&overwrite=false',
      'PUT /api/shelves/docs/content?path=%2Fa.txt&overwrite=true',
    ]);
  });

  it('reports any other refusal as failed, with its reason', async () => {
    stubServer(async () => json({ message: 'The shelf is full.', code: 'quota_exceeded' }, 507));

    await act(async () => uploads.upload([new File(['a'], 'a.txt')], '/'));
    await vi.waitFor(() => expect(last()?.status).toBe('failed'));

    expect(last()).toEqual({ status: 'failed', error: 'The shelf is full.' });
  });
});
