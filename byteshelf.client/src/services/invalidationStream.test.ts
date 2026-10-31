// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from './authService';
import { followInvalidations, INITIAL_RETRY_DELAY_MS, INVALIDATION_STREAM_PATH } from './invalidationStream';
import { AUTH_PATHS } from './authPaths';
import { installFakeWebLocks } from '../test/fakeWebLocks';

interface OpenStream {
  push: (text: string) => void;
  end: () => void;
}

/** A stream response the test writes to; aborting the request fails its reads, as a browser does. */
function streamResponse(signal: AbortSignal | null | undefined, streams: OpenStream[]): Response {
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder();
      streams.push({ push: (text) => controller.enqueue(encoder.encode(text)), end: () => controller.close() });
      signal?.addEventListener('abort', () => controller.error(new DOMException('The stream was closed.', 'AbortError')));
    },
  });
  return new Response(body, { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
}

describe('the live-update stream', () => {
  const streamTokens: string[] = [];
  const streams: OpenStream[] = [];
  const resources: string[] = [];
  let missed = 0;
  let streamAnswer: (signal: AbortSignal | null | undefined) => Promise<Response>;
  let close: () => void = () => undefined;

  beforeEach(() => {
    vi.useFakeTimers({ now: new Date('2026-10-31T12:00:00Z') });
    installFakeWebLocks();
    localStorage.clear();
    streamTokens.length = 0;
    streams.length = 0;
    resources.length = 0;
    missed = 0;
    streamAnswer = async (signal) => streamResponse(signal, streams);
    vi.stubGlobal('fetch', async (input: string, init?: RequestInit) => {
      if (input === AUTH_PATHS.REFRESH) {
        return new Response(JSON.stringify({ token: 'renewed', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), refreshToken: 'refresh-2' }), { status: 200 });
      }

      expect(input).toBe(INVALIDATION_STREAM_PATH);
      streamTokens.push(new Headers(init?.headers).get('Authorization') ?? '');
      // A reconnect loop that never waits would otherwise spin without letting the test go on.
      if (streamTokens.length > 20) return new Promise<Response>(() => undefined);
      return streamAnswer(init?.signal);
    });
  });

  afterEach(() => {
    close();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  function follow(): void {
    close = followInvalidations({ onResource: (resource) => resources.push(resource), onMissed: () => missed++ });
  }

  it('reports each resource the server publishes, across chunk boundaries', async () => {
    saveTokens({ token: 'first', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), refreshToken: 'refresh-1' });
    follow();
    await vi.advanceTimersByTimeAsync(0);

    streams[0].push(': heartbeat\n\ndata: {"resource":"shelves"}\n\ndata: {"resou');
    streams[0].push('rce":"users","metadata":null}\n\ndata: not json\n\n');
    await vi.advanceTimersByTimeAsync(0);

    expect(resources).toEqual(['shelves', 'users']);
    expect(missed).toBe(0);
  });

  it('waits before trying again when the server cannot be reached', async () => {
    saveTokens({ token: 'first', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), refreshToken: 'refresh-1' });
    streamAnswer = () => Promise.reject(new TypeError('Failed to fetch'));
    follow();
    await vi.advanceTimersByTimeAsync(0);

    await vi.advanceTimersByTimeAsync(2 * INITIAL_RETRY_DELAY_MS - 1);
    const attemptsBeforeTheWait = streamTokens.length;
    await vi.advanceTimersByTimeAsync(1);

    expect([attemptsBeforeTheWait, streamTokens.length]).toEqual([1, 2]);
  });

  it('refetches everything once the stream comes back after a drop', async () => {
    saveTokens({ token: 'first', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), refreshToken: 'refresh-1' });
    follow();
    await vi.advanceTimersByTimeAsync(0);

    streams[0].end();
    await vi.advanceTimersByTimeAsync(0);

    expect([streamTokens.length, missed]).toEqual([2, 1]);
  });

  it('reopens with a renewed token before the token runs out, without refetching everything', async () => {
    saveTokens({ token: 'first', expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(), refreshToken: 'refresh-1' });
    follow();
    await vi.advanceTimersByTimeAsync(0);

    await vi.advanceTimersByTimeAsync(4.5 * 60_000);

    expect(streamTokens).toEqual(['Bearer first', 'Bearer renewed']);
    expect(missed).toBe(0);
  });
});
