import type { paths } from '../types/api';
import { getExpiresAt, getFreshToken } from './authService';
import { getSseData, splitSseBuffer } from '../lib/sse';

export const INVALIDATION_STREAM_PATH = '/api/invalidation/stream' satisfies keyof paths;
export const INITIAL_RETRY_DELAY_MS = 1_000;
export const MAX_RETRY_DELAY_MS = 30_000;
/** How long a stream must have stayed open for its end to count as a clean close rather than a failure. */
export const STABLE_STREAM_MS = 30_000;

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
 * A failed attempt, or a stream that ends before STABLE_STREAM_MS, waits before the next, from
 * INITIAL_RETRY_DELAY_MS doubling up to MAX_RETRY_DELAY_MS. The server checks the token only when the
 * stream opens, so the stream is reopened shortly before the token runs out.
 */
export function followInvalidations(handlers: InvalidationHandlers): () => void {
  let disposed = false;
  // The open stream's controller, owned here so disposal can abort whichever stream is open.
  let connection: AbortController | null = null;
  // The wait before the next attempt; 0 reconnects at once.
  let retryDelay = 0;
  let hasConnected = false;
  // Set when the stream was closed to rotate the token: it was alive until then, so nothing was missed.
  let rotating = false;

  /** An attempt failed: wait longer before the next, and treat the next stream as a drop, not a rotation. */
  function failed(): void {
    retryDelay = Math.min(Math.max(retryDelay * 2, INITIAL_RETRY_DELAY_MS), MAX_RETRY_DELAY_MS);
    rotating = false;
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

  // One connection. It only sets the delay; run() owns the one sleep between attempts.
  async function runOnce(): Promise<void> {
    let token: string | null;
    try {
      token = await getFreshToken();
    } catch {
      failed();
      return;
    }

    // Disposed while the token was renewed: a stream opened now would have no one to close it.
    if (disposed) return;
    if (!token) {
      failed();
      return;
    }

    const abort = new AbortController();
    connection = abort;
    const rotation = scheduleRotation(abort);
    let openedAt: number | null = null;

    try {
      const response = await fetch(INVALIDATION_STREAM_PATH, {
        headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
        signal: abort.signal,
      });

      const reader = response.ok ? response.body?.getReader() : undefined;
      if (reader) {
        // Not on the first connection (queries fetch when they mount) nor after a rotation.
        if (hasConnected && !rotating) {
          handlers.onMissed();
        }
        hasConnected = true;
        rotating = false;
        openedAt = Date.now();

        await readEvents(reader, handlers.onResource);
      }
    } catch {
      // Unreachable, refused, broken or closed here: which of these it was is decided below.
    } finally {
      clearTimeout(rotation);
      connection = null;
    }

    if (disposed) return;
    if (abort.signal.aborted) {
      // Closed by the rotation timer, the one other thing that aborts it: reconnect at once.
      retryDelay = 0;
    } else if (openedAt !== null && Date.now() - openedAt >= STABLE_STREAM_MS) {
      // It worked for a while, so this end, such as a proxy closing a long stream, is no failure.
      retryDelay = 0;
    } else {
      failed();
    }
  }

  async function run(): Promise<void> {
    while (!disposed) {
      await runOnce();
      if (disposed) return;
      if (retryDelay > 0) {
        await sleep(retryDelay);
      }
    }
  }

  void run();

  return () => {
    disposed = true;
    connection?.abort();
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
