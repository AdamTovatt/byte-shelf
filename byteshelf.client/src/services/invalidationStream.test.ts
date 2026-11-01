// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { saveTokens } from './authService';
import { followInvalidations, INITIAL_RETRY_DELAY_MS, INVALIDATION_STREAM_PATH, MAX_RETRY_DELAY_MS, STABLE_STREAM_MS } from './invalidationStream';
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

function renewedTokens(): Response {
  return new Response(JSON.stringify({ token: 'renewed', expiresAt: new Date(Date.now() + 3_600_000).toISOString(), refreshToken: 'refresh-2' }), { status: 200 });
}

describe('the live-update stream', () => {
  const streamTokens: string[] = [];
  const streams: OpenStream[] = [];
  const resources: string[] = [];
  // Requests to anything else: recorded rather than asserted where they arrive, since the code under
  // test catches what its fetch throws.
  const unexpected: string[] = [];
  let missed = 0;
  let streamAnswer: (signal: AbortSignal | null | undefined) => Promise<Response>;
  let refreshAnswer: () => Promise<Response>;
  let close: () => void = () => undefined;

  beforeEach(() => {
    vi.useFakeTimers({ now: new Date('2026-10-31T12:00:00Z') });
    installFakeWebLocks();
    localStorage.clear();
    streamTokens.length = 0;
    streams.length = 0;
    resources.length = 0;
    unexpected.length = 0;
    missed = 0;
    streamAnswer = async (signal) => streamResponse(signal, streams);
    refreshAnswer = async () => renewedTokens();
    vi.stubGlobal('fetch', async (input: string, init?: RequestInit) => {
      if (input === AUTH_PATHS.REFRESH) return refreshAnswer();
      if (input !== INVALIDATION_STREAM_PATH) {
        unexpected.push(input);
        return new Response(null, { status: 404 });
      }

      streamTokens.push(new Headers(init?.headers).get('Authorization') ?? '');
      // A reconnect loop that never waits would otherwise spin without letting the test go on.
      if (streamTokens.length > 20) return new Promise<Response>(() => undefined);
      return streamAnswer(init?.signal);
    });
  });

  afterEach(() => {
    close();
    close = () => undefined;
    vi.useRealTimers();
    vi.unstubAllGlobals();
    expect(unexpected).toEqual([]);
  });

  function follow(): void {
    close = followInvalidations({ onResource: (resource) => resources.push(resource), onMissed: () => missed++ });
  }

  function signIn(expiresInMs: number = 3_600_000): void {
    saveTokens({ token: 'first', expiresAt: new Date(Date.now() + expiresInMs).toISOString(), refreshToken: 'refresh-1' });
  }

  it('reports each resource the server publishes, across chunk boundaries', async () => {
    signIn();
    follow();
    await vi.advanceTimersByTimeAsync(0);

    streams[0].push(': heartbeat\n\ndata: {"resource":"shelves"}\n\ndata: {"resou');
    streams[0].push('rce":"users","metadata":null}\n\ndata: not json\n\n');
    await vi.advanceTimersByTimeAsync(0);

    expect(resources).toEqual(['shelves', 'users']);
    expect(missed).toBe(0);
  });

  it('waits before trying again when the server cannot be reached, longer each time', async () => {
    signIn();
    streamAnswer = () => Promise.reject(new TypeError('Failed to fetch'));
    follow();
    await vi.advanceTimersByTimeAsync(0);

    const attempts: number[] = [streamTokens.length];
    for (const wait of [INITIAL_RETRY_DELAY_MS, 2 * INITIAL_RETRY_DELAY_MS]) {
      await vi.advanceTimersByTimeAsync(wait - 1);
      attempts.push(streamTokens.length);
      await vi.advanceTimersByTimeAsync(1);
      attempts.push(streamTokens.length);
    }

    expect(attempts).toEqual([1, 1, 2, 2, 3]);
  });

  it('waits before reopening a stream that ended at once, instead of reconnecting in a loop', async () => {
    signIn();
    streamAnswer = async (signal) => {
      const response = streamResponse(signal, streams);
      streams[streams.length - 1].end();
      return response;
    };
    follow();
    await vi.advanceTimersByTimeAsync(0);

    expect(streamTokens).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(INITIAL_RETRY_DELAY_MS);
    expect(streamTokens).toHaveLength(2);
  });

  it('reopens at once, and refetches everything, after a stream that stayed open a while ends', async () => {
    signIn();
    follow();
    await vi.advanceTimersByTimeAsync(STABLE_STREAM_MS);

    streams[0].end();
    await vi.advanceTimersByTimeAsync(0);

    expect([streamTokens.length, missed]).toEqual([2, 1]);
  });

  it('refetches everything once the stream comes back after a short-lived one dropped', async () => {
    signIn();
    follow();
    await vi.advanceTimersByTimeAsync(0);

    streams[0].end();
    await vi.advanceTimersByTimeAsync(INITIAL_RETRY_DELAY_MS);

    expect([streamTokens.length, missed]).toEqual([2, 1]);
  });

  it('reopens with a renewed token before the token runs out, without refetching everything', async () => {
    signIn(5 * 60_000);
    follow();
    await vi.advanceTimersByTimeAsync(0);

    await vi.advanceTimersByTimeAsync(4.5 * 60_000);

    expect(streamTokens).toEqual(['Bearer first', 'Bearer renewed']);
    expect(missed).toBe(0);
  });

  it('keeps trying when renewing the token for the stream throws', async () => {
    signIn(-1_000);
    refreshAnswer = async () => {
      refreshAnswer = async () => renewedTokens();
      return new Response('not json', { status: 200 });
    };
    follow();
    await vi.advanceTimersByTimeAsync(0);

    expect(streamTokens).toEqual([]);
    await vi.advanceTimersByTimeAsync(INITIAL_RETRY_DELAY_MS);
    expect(streamTokens).toEqual(['Bearer renewed']);
  });

  it('opens no stream when closed while the token is being renewed', async () => {
    signIn(-1_000);
    let answerRefresh: (response: Response) => void = () => undefined;
    refreshAnswer = () => new Promise<Response>((resolve) => { answerRefresh = resolve; });
    follow();
    await vi.advanceTimersByTimeAsync(0);

    close();
    answerRefresh(renewedTokens());
    await vi.advanceTimersByTimeAsync(MAX_RETRY_DELAY_MS);

    expect(streamTokens).toEqual([]);
  });
});
