import { describe, expect, it, vi } from 'vitest';
import { blobReader } from './contentReader';
import { openingReader } from './openingReader';

describe('a reader that opens another', () => {
  const opened = blobReader(new Blob(['opened']));
  const taken = blobReader(new Blob(['taken']));

  it('reads the one it takes, opening none', async () => {
    const open = vi.fn(async () => opened);
    const reader = openingReader(open);

    reader.take(taken);

    expect(await (await reader.blob()).text()).toBe('taken');
    expect(open).not.toHaveBeenCalled();
  });

  it('reads on the one that is open, whatever it takes after that', async () => {
    const reader = openingReader(async () => opened);
    await reader.blob();

    reader.take(taken);

    expect(await (await reader.blob()).text()).toBe('opened');
  });

  it('reads on the one that is opening, whatever it takes meanwhile', async () => {
    const reader = openingReader(async () => opened);
    const first = reader.blob();

    reader.take(taken);

    expect(await (await first).text()).toBe('opened');
    expect(await (await reader.blob()).text()).toBe('opened');
  });

  it('reads the one it takes once the one that is opening fails', async () => {
    let fail: (reason: Error) => void = () => undefined;
    const reader = openingReader(() => new Promise((_, reject) => { fail = reject; }));
    const first = reader.blob().catch((error: Error) => error.message);

    reader.take(taken);
    fail(new Error('It is locked.'));

    expect(await first).toBe('It is locked.');
    expect(await (await reader.blob()).text()).toBe('taken');
  });
});
