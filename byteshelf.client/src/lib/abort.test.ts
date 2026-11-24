import { expect, it } from 'vitest';
import { untilAborted } from './abort';

it('waits for nothing once its signal has aborted', async () => {
  await expect(untilAborted(new Promise(() => undefined), AbortSignal.abort())).rejects.toMatchObject({ name: 'AbortError' });
});

it('answers as the work does, or fails as it fails', async () => {
  const { signal } = new AbortController();

  expect(await untilAborted(Promise.resolve('done'), signal)).toBe('done');
  await expect(untilAborted(Promise.reject(new Error('It failed.')), signal)).rejects.toThrow('It failed.');
});
