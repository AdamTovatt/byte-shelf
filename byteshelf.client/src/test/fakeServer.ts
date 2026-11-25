import { vi } from 'vitest';

/** A JSON response, as the server answers. */
export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

/** An hour from now, as the server writes an access token's expiry. */
export function inAnHour(): string {
  return new Date(Date.now() + 60 * 60_000).toISOString();
}

/** Where the page is, for the addresses requests give relative to it. */
const ORIGIN = 'http://shelf.test';

/**
 * Stubs the global fetch and XMLHttpRequest with `answer`, recording each request as "METHOD /path?query Bearer token".
 * An XMLHttpRequest tells of half its body sent, then all of it, before it is answered; aborted before all of it is
 * told sent, it reaches nothing.
 * Undo with vi.unstubAllGlobals().
 */
export function stubServer(answer: (request: Request) => Promise<Response>): string[] {
  const sent: string[] = [];

  function take(request: Request): Promise<Response> {
    const url = new URL(request.url);
    sent.push(`${request.method} ${url.pathname}${url.search} ${request.headers.get('Authorization') ?? ''}`.trim());
    return answer(request);
  }

  vi.stubGlobal('fetch', (input: Request | string, init?: RequestInit) => take(input instanceof Request ? input : new Request(new URL(input, ORIGIN), init)));
  vi.stubGlobal('XMLHttpRequest', fakeXhr(take));
  return sent;
}

type Handler = ((event: ProgressEvent) => void) | null;

/** An XMLHttpRequest that goes to `take`, with as much of the real one as the app uses. */
function fakeXhr(take: (request: Request) => Promise<Response>) {
  return class FakeXhr {
    readonly upload = { onprogress: null as Handler };
    onload: Handler = null;
    onerror: Handler = null;
    ontimeout: Handler = null;
    onabort: Handler = null;
    responseType = '';
    status = 0;
    statusText = '';
    response: Blob | null = null;
    private method = 'GET';
    private url = '';
    private readonly headers = new Headers();
    private answerHeaders = new Headers();
    private aborted = false;

    open(method: string, url: string): void {
      this.method = method;
      this.url = url;
    }

    setRequestHeader(name: string, value: string): void {
      this.headers.set(name, value);
    }

    getAllResponseHeaders(): string {
      return Array.from(this.answerHeaders, ([name, value]) => `${name}: ${value}\r\n`).join('');
    }

    send(body: Blob): void {
      const request = new Request(new URL(this.url, ORIGIN), { method: this.method, headers: this.headers, body });
      const total = body.size;
      for (const loaded of [Math.floor(total / 2), total]) this.upload.onprogress?.(new ProgressEvent('progress', { loaded, total, lengthComputable: true }));
      // Aborted while it was sent, the server never had all of it.
      if (this.aborted) return;
      take(request).then(async (response) => {
        const contents = await response.blob();
        if (this.aborted) return;
        this.status = response.status;
        this.statusText = response.statusText;
        this.answerHeaders = response.headers;
        this.response = contents;
        this.onload?.(new ProgressEvent('load'));
      }, () => {
        if (!this.aborted) this.onerror?.(new ProgressEvent('error'));
      });
    }

    abort(): void {
      if (this.aborted) return;
      this.aborted = true;
      this.onabort?.(new ProgressEvent('abort'));
    }
  };
}
