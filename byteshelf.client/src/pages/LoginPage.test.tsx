// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LoginPage } from './LoginPage';
import { button, field, fill, press } from '../test/dom';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const login = vi.hoisted(() => vi.fn());
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({ login }) }));

const TIMEOUT_MS = 300_000;

function ceremony(id: string) {
  return { ceremonyId: id, options: { challenge: 'AQID', rpId: 'localhost', allowCredentials: [], userVerification: 'required', timeout: TIMEOUT_MS } };
}

const tokens = { token: 'access', expiresAt: inAnHour(), refreshToken: 'refresh' };

/** A passkey the browser answers with. */
const answered = { toJSON: () => ({ id: 'credential-1', response: {} }) };

const FAILED = 'Could not sign in with that passkey';

/** Lets pending promises and the effects they start run. */
async function settle(): Promise<void> {
  for (let round = 0; round < 5; round++) await act(async () => {});
}

describe('the sign-in page with passkeys', () => {
  let root: Root;
  let get: ReturnType<typeof vi.fn>;
  let autofill: boolean;
  let begun: number;

  /** Shows the page against a server answering a new ceremony to each begin, and `signIn` to each finish. */
  async function show(signIn: () => Response = () => json(tokens)): Promise<string[]> {
    const sent = stubServer(async (request) => {
      if (new URL(request.url).pathname === '/api/auth/passkey') return signIn();
      begun++;
      return json(ceremony(`ceremony-${begun}`));
    });
    await act(async () => root.render(<MemoryRouter><LoginPage /></MemoryRouter>));
    await settle();
    return sent;
  }

  /** The ceremony ids the suggestion requests were made with, in order. */
  function suggestionCalls(): Array<{ signal: AbortSignal }> {
    return get.mock.calls.map((call) => call[0]).filter((options) => options.mediation === 'conditional');
  }

  beforeEach(() => {
    login.mockReset();
    autofill = false;
    begun = 0;
    get = vi.fn();
    vi.stubGlobal('PublicKeyCredential', class {
      static isConditionalMediationAvailable(): Promise<boolean> {
        return Promise.resolve(autofill);
      }

      static parseRequestOptionsFromJSON(json: { challenge: string }): unknown {
        return { parsedChallenge: json.challenge };
      }

      static parseCreationOptionsFromJSON(json: unknown): unknown {
        return json;
      }

      toJSON(): void {}
    });
    Object.defineProperty(navigator, 'credentials', { value: { get }, configurable: true });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows no passkey button while the server has passkeys off', async () => {
    stubServer(async () => json({ message: 'Passkeys are off' }, 404));
    await act(async () => root.render(<MemoryRouter><LoginPage /></MemoryRouter>));
    await settle();

    expect(button('Sign in with passkey')).toBeNull();
    expect(button('Sign in')).not.toBeNull();
  });

  it('signs in with the passkey the browser answers with', async () => {
    get.mockResolvedValue(answered);
    const sent = await show();

    await press('Sign in with passkey');

    expect(login).toHaveBeenCalledWith(tokens);
    expect(sent).toContain('POST /api/auth/passkey');
    expect(get.mock.calls[0][0]).toMatchObject({ publicKey: { parsedChallenge: 'AQID' }, mediation: undefined });
  });

  it('says nothing when the prompt is closed', async () => {
    get.mockRejectedValue(new DOMException('The prompt was closed.', 'NotAllowedError'));
    await show();

    await press('Sign in with passkey');

    expect(document.body.textContent).not.toContain(FAILED);
    expect(login).not.toHaveBeenCalled();
  });

  it('says one line and leaves the password form as it is when the server refuses the passkey', async () => {
    get.mockResolvedValue(answered);
    await show(() => json({ message: 'no' }, 401));
    fill('Email', 'ada@example.com');
    fill('Password', 'typed so far');

    await press('Sign in with passkey');

    expect(document.body.textContent).toContain(FAILED);
    expect([field('Email').value, field('Password').value]).toEqual(['ada@example.com', 'typed so far']);
    expect(login).not.toHaveBeenCalled();
  });

  it('says the same one line when the sign-in is rate limited', async () => {
    get.mockResolvedValue(answered);
    await show(() => json({ message: 'Too many requests' }, 429));

    await press('Sign in with passkey');

    expect(document.body.textContent).toContain(FAILED);
  });

  describe('suggestions in the email field', () => {
    beforeEach(() => {
      autofill = true;
    });

    it('offers the saved passkeys there', async () => {
      get.mockReturnValue(new Promise(() => {}));
      await show();

      expect(suggestionCalls()).toHaveLength(1);
      expect(field('Email').getAttribute('autocomplete')).toBe('username webauthn');
    });

    it('says nothing when the browser refuses them at once, and does not ask again', async () => {
      get.mockRejectedValue(new DOMException('This page is not the server\'s host.', 'SecurityError'));
      await show();

      expect(document.body.textContent).not.toContain(FAILED);
      expect(suggestionCalls()).toHaveLength(1);
    });

    it('says the line and offers them again when the server refuses the one picked', async () => {
      get.mockResolvedValueOnce(answered).mockReturnValue(new Promise(() => {}));
      await show(() => json({ message: 'no' }, 401));

      expect(document.body.textContent).toContain(FAILED);
      expect(suggestionCalls()).toHaveLength(2);
      expect(begun).toBe(2);
    });

    it('asks for a new ceremony before the one offered runs out', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      get.mockImplementation(({ signal }: { signal: AbortSignal }) => new Promise((_, reject) => {
        signal.addEventListener('abort', () => reject(new DOMException('Stopped.', 'AbortError')));
      }));
      await show();

      await act(async () => vi.advanceTimersByTime(TIMEOUT_MS - 1));
      await settle();

      expect(suggestionCalls()).toHaveLength(2);
      expect(suggestionCalls()[0].signal.aborted).toBe(true);
      expect(document.body.textContent).not.toContain(FAILED);
    });

    it('stops them without a word when the button is pressed', async () => {
      get.mockImplementationOnce(({ signal }: { signal: AbortSignal }) => new Promise((_, reject) => {
        signal.addEventListener('abort', () => reject(new DOMException('Stopped.', 'AbortError')));
      }));
      get.mockResolvedValueOnce(answered);
      await show();

      await press('Sign in with passkey');

      expect(suggestionCalls()[0].signal.aborted).toBe(true);
      expect(document.body.textContent).not.toContain(FAILED);
      expect(login).toHaveBeenCalledWith(tokens);
    });
  });
});
