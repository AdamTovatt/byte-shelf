// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { loadImage } from './loadImage';

describe('loading an image', () => {
  let image: HTMLImageElement;
  let loading: AbortController;

  beforeEach(() => {
    image = document.createElement('img');
    image.decode = vi.fn(() => Promise.resolve());
    loading = new AbortController();
  });

  it('is done once it has been decoded', async () => {
    let decoded = () => {};
    image.decode = vi.fn(() => new Promise<void>((resolve) => (decoded = resolve)));
    const done = vi.fn();
    void loadImage(image, 'a.png', loading.signal).then(done);
    expect(image.getAttribute('src')).toBe('a.png');

    image.dispatchEvent(new Event('load'));
    await vi.waitFor(() => expect(image.decode).toHaveBeenCalled());
    expect(done).not.toHaveBeenCalled();
    decoded();

    await vi.waitFor(() => expect(done).toHaveBeenCalled());
  });

  it('is done of one that was loaded and is not decoded ahead', async () => {
    image.decode = () => Promise.reject(new Error('It is too large.'));
    const loaded = loadImage(image, 'a.png', loading.signal);
    image.dispatchEvent(new Event('load'));

    await expect(loaded).resolves.toBeUndefined();
  });

  it('ends as it is aborted while it is decoded', async () => {
    image.decode = vi.fn(() => new Promise<void>(() => {}));
    const loaded = loadImage(image, 'a.png', loading.signal);
    image.dispatchEvent(new Event('load'));
    await vi.waitFor(() => expect(image.decode).toHaveBeenCalled());

    loading.abort();

    await expect(loaded).rejects.toThrow('aborted');
    expect(image.hasAttribute('src')).toBe(false);
  });

  it('fails of one that does not load', async () => {
    const loaded = loadImage(image, 'a.png', loading.signal);
    image.dispatchEvent(new Event('error'));

    await expect(loaded).rejects.toThrow('could not be loaded');
  });

  it('ends as it is aborted, which takes its address away', async () => {
    const loaded = loadImage(image, 'a.png', loading.signal);
    loading.abort();

    await expect(loaded).rejects.toThrow('aborted');
    expect(image.hasAttribute('src')).toBe(false);
    await expect(loadImage(image, 'b.png', loading.signal)).rejects.toThrow('aborted');
    expect(image.hasAttribute('src')).toBe(false);
  });
});
