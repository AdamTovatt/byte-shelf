import { vi } from 'vitest';

/** A JSON response, as the server answers. */
export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

/** An hour from now, as the server writes an access token's expiry. */
export function inAnHour(): string {
  return new Date(Date.now() + 60 * 60_000).toISOString();
}

/**
 * Stubs the global fetch with `answer`, recording each request as "METHOD /path?query Bearer token".
 * Undo with vi.unstubAllGlobals().
 */
export function stubServer(answer: (request: Request) => Promise<Response>): string[] {
  const sent: string[] = [];
  vi.stubGlobal('fetch', (input: Request | string, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(input, 'http://shelf.test'), init);
    const url = new URL(request.url);
    sent.push(`${request.method} ${url.pathname}${url.search} ${request.headers.get('Authorization') ?? ''}`.trim());
    return answer(request);
  });
  return sent;
}
