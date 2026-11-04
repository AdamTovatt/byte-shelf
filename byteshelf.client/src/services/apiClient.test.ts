// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authorizedFetch } from './apiClient';
import { saveTokens } from './authService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

const challenged = () => new Response(null, { status: 401, headers: { 'WWW-Authenticate': 'Bearer error="invalid_token"' } });

describe('authorized requests', () => {
  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renews and resends a request whose token the server challenged', async () => {
    const sent = stubServer(async (request) => {
      if (new URL(request.url).pathname === '/api/auth/refresh') return json({ token: 'second', expiresAt: inAnHour(), refreshToken: 'refresh-2' });
      return request.headers.get('Authorization') === 'Bearer first' ? challenged() : json({});
    });

    const response = await authorizedFetch('/api/users/me', { method: 'GET' });

    expect(response.status).toBe(200);
    expect(sent).toEqual(['GET /api/users/me Bearer first', 'POST /api/auth/refresh', 'GET /api/users/me Bearer second']);
  });

  it('answers a 401 without a challenge as it is, such as a wrong current password', async () => {
    const sent = stubServer(async () => json({ message: 'The current password is incorrect.', code: 'invalid_credentials' }, 401));

    const response = await authorizedFetch('/api/users/me/password', { method: 'PUT' });

    expect(response.status).toBe(401);
    expect(sent).toEqual(['PUT /api/users/me/password Bearer first']);
    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-1');
  });

  it('keeps the session when the server cannot renew it just now', async () => {
    stubServer(async (request) => (new URL(request.url).pathname === '/api/auth/refresh' ? new Response(null, { status: 503 }) : challenged()));

    const response = await authorizedFetch('/api/users/me', { method: 'GET' });

    expect(response.status).toBe(401);
    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBe('refresh-1');
  });

  it('forgets the session when the server has ended it', async () => {
    stubServer(async (request) => (new URL(request.url).pathname === '/api/auth/refresh' ? new Response(null, { status: 401 }) : challenged()));

    await authorizedFetch('/api/users/me', { method: 'GET' });

    expect(localStorage.getItem('byteshelf_auth_refresh_token')).toBeNull();
  });
});
