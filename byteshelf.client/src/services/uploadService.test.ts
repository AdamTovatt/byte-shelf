// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from './authService';
import { CHUNK_BYTES, uploadFile } from './uploadService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

const storedFile = { id: 'f', path: '/big.bin', name: 'big.bin', sizeBytes: 1, contentType: 'application/octet-stream' };

/** The method and route of each request, without its token. */
function routes(sent: string[]): string[] {
  return sent.map((line) => line.split(' Bearer')[0]);
}

/** A server that starts upload u1, takes every chunk, and completes it. */
async function acceptingServer(request: Request): Promise<Response> {
  const path = new URL(request.url).pathname;
  if (request.method === 'POST' && path.endsWith('/uploads')) return json({ id: 'u1' });
  if (path.endsWith('/complete')) return json(storedFile);
  if (request.method === 'PUT' && path.endsWith('/content')) return json(storedFile);
  return json({});
}

describe('uploads', () => {
  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends a file of one chunk or less in one request', async () => {
    const sent = stubServer(acceptingServer);
    const progress: number[] = [];

    const stored = await uploadFile({ shelf: 'docs', path: '/a b.bin', file: new Blob([new Uint8Array(CHUNK_BYTES)]), overwrite: true, onProgress: (bytes) => progress.push(bytes) });

    expect(stored).toEqual(storedFile);
    expect(routes(sent)).toEqual(['PUT /api/shelves/docs/content?path=%2Fa+b.bin&overwrite=true']);
    expect(progress).toEqual([CHUNK_BYTES]);
  });

  it('sends a file larger than a chunk in chunks, then completes it', async () => {
    const sent = stubServer(acceptingServer);
    const progress: number[] = [];

    await uploadFile({ shelf: 'docs', path: '/big.bin', file: new Blob([new Uint8Array(CHUNK_BYTES + 1)]), overwrite: false, onProgress: (bytes) => progress.push(bytes) });

    expect(routes(sent)).toEqual([
      'POST /api/shelves/docs/uploads',
      'PUT /api/shelves/docs/uploads/u1?offset=0',
      `PUT /api/shelves/docs/uploads/u1?offset=${CHUNK_BYTES}`,
      'POST /api/shelves/docs/uploads/u1/complete',
    ]);
    expect(progress).toEqual([CHUNK_BYTES, CHUNK_BYTES + 1]);
  });

  it('marks an encrypted file encrypted, in the query of one request or the start of chunks, and a plain one in neither', async () => {
    const bodies: unknown[] = [];
    const sent = stubServer(async (request) => {
      if (request.method === 'POST' && new URL(request.url).pathname.endsWith('/uploads')) bodies.push(await request.json());
      return acceptingServer(request);
    });

    await uploadFile({ shelf: 'docs', path: '/a', file: new Blob(['a']), overwrite: false, encrypted: true });
    await uploadFile({ shelf: 'docs', path: '/b', file: new Blob(['b']), overwrite: false, encrypted: false });
    await uploadFile({ shelf: 'docs', path: '/big', file: new Blob([new Uint8Array(CHUNK_BYTES + 1)]), overwrite: false, encrypted: true });

    expect(routes(sent).slice(0, 2)).toEqual([
      'PUT /api/shelves/docs/content?path=%2Fa&overwrite=false&encrypted=true',
      'PUT /api/shelves/docs/content?path=%2Fb&overwrite=false',
    ]);
    expect(bodies).toEqual([expect.objectContaining({ path: '/big', encrypted: true })]);
  });

  it('aborts the upload when a chunk fails, and throws the failure', async () => {
    const sent = stubServer(async (request) => {
      if (request.method === 'PUT') return json({ message: 'Disk full.', code: 'quota_exceeded' }, 507);
      return acceptingServer(request);
    });

    await expect(uploadFile({ shelf: 'docs', path: '/big.bin', file: new Blob([new Uint8Array(CHUNK_BYTES + 1)]), overwrite: false }))
      .rejects.toMatchObject({ code: 'quota_exceeded', status: 507 });
    await vi.waitFor(() => expect(routes(sent)).toContain('DELETE /api/shelves/docs/uploads/u1'));
  });

  it('aborts the upload when completing it fails', async () => {
    const sent = stubServer(async (request) => {
      if (new URL(request.url).pathname.endsWith('/complete')) return json({ message: 'The file changed.', code: 'conflict' }, 409);
      return acceptingServer(request);
    });

    await expect(uploadFile({ shelf: 'docs', path: '/big.bin', file: new Blob([new Uint8Array(CHUNK_BYTES + 1)]), overwrite: false }))
      .rejects.toMatchObject({ code: 'conflict', status: 409 });
    await vi.waitFor(() => expect(routes(sent)).toContain('DELETE /api/shelves/docs/uploads/u1'));
  });
});
