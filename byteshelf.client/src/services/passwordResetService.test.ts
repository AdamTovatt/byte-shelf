import { afterEach, describe, expect, it, vi } from 'vitest';
import { completePasswordReset, requestPasswordReset } from './passwordResetService';
import { json, stubServer } from '../test/fakeServer';

describe('password reset', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /** Stubs the server with `status`, recording each request as "METHOD /path body" (no token). */
  function serve(status: number): string[] {
    const received: string[] = [];
    stubServer(async (request) => {
      received.push(`${request.method} ${new URL(request.url).pathname} ${await request.text()} ${request.headers.get('Authorization') ?? ''}`.trim());
      return status === 204 ? new Response(null, { status }) : json({ message: 'refused' }, status);
    });
    return received;
  }

  it('asks the server to send a reset link', async () => {
    const received = serve(204);

    await requestPasswordReset('ada@example.com');

    expect(received).toEqual(['POST /api/password-reset/request {"email":"ada@example.com"}']);
  });

  it('says so when the link could not be asked for', async () => {
    serve(500);

    await expect(requestPasswordReset('ada@example.com')).rejects.toThrow('Failed to request a password reset');
  });

  it('sets the new password with the link\'s token', async () => {
    const received = serve(204);

    await completePasswordReset('token-1', 'a new password');

    expect(received).toEqual(['POST /api/password-reset/complete {"token":"token-1","newPassword":"a new password"}']);
  });

  it('tells an invalid or expired link apart from other failures', async () => {
    serve(400);
    await expect(completePasswordReset('token-1', 'a new password')).rejects.toThrow('invalid or has expired');

    serve(500);
    await expect(completePasswordReset('token-1', 'a new password')).rejects.toThrow('Something went wrong');
  });
});
