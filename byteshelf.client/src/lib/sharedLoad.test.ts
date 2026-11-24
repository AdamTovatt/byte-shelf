import { describe, expect, it } from 'vitest';
import { sharedLoad } from './sharedLoad';

describe('a shared load', () => {
  interface Started {
    key: string;
    signal: AbortSignal;
    resolve: (value: string | null) => void;
    reject: (error: Error) => void;
  }

  function loads() {
    const started: Started[] = [];
    const share = sharedLoad<string>();
    const join = (key: string, signal?: AbortSignal) => share(key, (loading) => new Promise((resolve, reject) => started.push({ key, signal: loading, resolve, reject })), signal);
    return { started, join };
  }

  it('is started once for all who join it, and kept while someone wants what it answered', async () => {
    const { started, join } = loads();
    const preview = new AbortController();
    const first = join('a', preview.signal);
    const second = join('a');

    started[0].resolve('A');

    expect(await Promise.all([first, second, join('a')])).toEqual(['A', 'A', 'A']);
    expect(started).toHaveLength(1);
    preview.abort();
    void join('a');
    expect(started).toHaveLength(2);
  });

  it('is aborted once everyone who joined has, and started again by the next to join', async () => {
    const { started, join } = loads();
    const [one, other] = [new AbortController(), new AbortController()];
    void join('a', one.signal);
    void join('a', other.signal);

    one.abort();
    expect(started[0].signal.aborted).toBe(false);
    other.abort();
    expect(started[0].signal.aborted).toBe(true);
    void join('a');
    expect(started).toHaveLength(2);
  });

  it('goes on for one who gave no signal, and what it answered is then let go', async () => {
    const { started, join } = loads();
    const preview = new AbortController();
    void join('a', preview.signal);
    const saved = join('a');

    preview.abort();
    expect(started[0].signal.aborted).toBe(false);
    started[0].resolve('A');

    expect(await saved).toBe('A');
    void join('a');
    expect(started).toHaveLength(2);
  });

  it('is not kept when it failed or answered null', async () => {
    const { started, join } = loads();
    const { signal } = new AbortController();
    const failing = join('a', signal);
    started[0].reject(new Error('unreachable'));
    await expect(failing).rejects.toThrow('unreachable');

    const locked = join('a', signal);
    started[1].resolve(null);
    expect(await locked).toBeNull();

    void join('a', signal);
    expect(started).toHaveLength(3);
  });

  it('gives way to another key\'s, going on for whoever wants it', async () => {
    const { started, join } = loads();
    const first = join('a');
    const second = join('b');

    started[0].resolve('A');
    started[1].resolve('B');

    expect(started.map(({ key }) => key)).toEqual(['a', 'b']);
    expect(await Promise.all([first, second])).toEqual(['A', 'B']);
  });
});
