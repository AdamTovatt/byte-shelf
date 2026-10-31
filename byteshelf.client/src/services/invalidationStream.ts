import { getExpiresAt, getFreshToken } from './authService';
import { getSseData, splitSseBuffer } from '../lib/sse';

export const INVALIDATION_STREAM_PATH = '/api/invalidation/stream';
export const INITIAL_RETRY_DELAY_MS = 1_000;
export const MAX_RETRY_DELAY_MS = 30_000;

/** How long before the access token runs out the stream is reopened with a renewed one. */
const ROTATE_AHEAD_MS = 30_000;

export interface InvalidationHandlers {
  /** A resource the server published as changed. */
  onResource: (resource: string) => void;
  /** The stream came back after an unexpected drop, so changes made meanwhile may have been missed. */
  onMissed: () => void;
}

/**
 * Holds the server's live-update stream open and reports each published resource; answers a function
 * that closes it. EventSource cannot send the session's token, so this reads the response with fetch.
 * A failed attempt waits before the next, doubling up to MAX_RETRY_DELAY_MS. The server checks the
 * token only when the stream opens, so the stream is reopened shortly before the token runs out.
 */
export function followInvalidations(handlers: InvalidationHandlers): () => void {
  let disposed = false;
  let currentAbort: AbortController | null = null;
  let retryDelay = INITIAL_RETRY_DELAY_MS;
  let hasConnected = false;
  // Set when the stream was closed to rotate the token: it was alive until then, so nothing was missed.
  let rotating = false;

  function escalateRetryDelay(): void {
    retryDelay = Math.min(retryDelay * 2, MAX_RETRY_DELAY_MS);
  }

  function scheduleRotation(abort: AbortController): ReturnType<typeof setTimeout> | undefined {
    const expiresAt = getExpiresAt();
    if (!expiresAt) return undefined;
    const delay = Math.max(new Date(expiresAt).getTime() - Date.now() - ROTATE_AHEAD_MS, 0);
    return setTimeout(() => {
      rotating = true;
      abort.abort();
    }, delay);
  }

  // One connection. Failures only escalate the delay; run() owns the one sleep between attempts.
  async function runOnce(): Promise<void> {
    const token = await getFreshToken();
    if (!token) {
      escalateRetryDelay();
      return;
    }

    const abort = new AbortController();
    currentAbort = abort;
    const rotation = scheduleRotation(abort);

    try {
      const response = await fetch(INVALIDATION_STREAM_PATH, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
        signal: abort.signal,
      });

      const reader = response.ok ? response.body?.getReader() : undefined;
      if (!reader) {
        escalateRetryDelay();
        return;
      }

      retryDelay = INITIAL_RETRY_DELAY_MS;
      // Not on the first connection (queries fetch when they mount) nor after a rotation.
      if (hasConnected && !rotating) {
        handlers.onMissed();
      }
      hasConnected = true;
      rotating = false;

      await readEvents(reader, handlers.onResource);
    } catch {
      // A closed stream (disposal or rotation) reconnects at once; anything else, such as the server
      // being unreachable, waits first.
      if (!abort.signal.aborted) {
        escalateRetryDelay();
      }
    } finally {
      clearTimeout(rotation);
    }
  }

  async function run(): Promise<void> {
    while (!disposed) {
      await runOnce();
      if (disposed) return;
      // At the initial delay the last stream ended cleanly or was rotated: reconnect at once.
      if (retryDelay > INITIAL_RETRY_DELAY_MS) {
        await sleep(retryDelay);
      }
    }
  }

  void run();

  return () => {
    disposed = true;
    currentAbort?.abort();
  };
}

async function readEvents(reader: ReadableStreamDefaultReader<Uint8Array>, onResource: (resource: string) => void): Promise<void> {
  const decoder = new TextDecoder();
  let buffer = '';
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) return;

      buffer += decoder.decode(value, { stream: true });
      const { completeBlocks, remainder } = splitSseBuffer(buffer);
      buffer = remainder;

      for (const block of completeBlocks) {
        const data = getSseData(block);
        if (!data) continue;

        let resource: unknown;
        try {
          resource = (JSON.parse(data) as { resource?: unknown }).resource;
        } catch {
          continue;
        }

        if (typeof resource === 'string') onResource(resource);
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
