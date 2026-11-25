import { afterEach, describe, expect, it, vi } from 'vitest';
import { showsType } from './showsType';

// What was answered of a type is kept for the page's life, so each test asks of types no other does.
describe('whether a browser shows a type', () => {
  afterEach(() => vi.restoreAllMocks());

  /** Has the browser choose the sources of a picture whose type it shows. */
  function shows(...types: string[]): void {
    vi.spyOn(HTMLImageElement.prototype, 'currentSrc', 'get').mockImplementation(function (this: HTMLImageElement) {
      const source = this.parentElement!.querySelector('source')!;
      return types.includes(source.type) ? source.srcset : this.src;
    });
  }

  // What a browser chooses is faked here as Chromium chooses it, so this proves only how the answer is read
  // from the choice; that Chromium chooses so for JPEG, PNG, WebP and AVIF and not for HEIC or TIFF was seen in it.
  it('is whether it chooses a source of that type for a picture', async () => {
    shows('image/jpeg');

    expect(await Promise.all(['image/jpeg', 'image/heic'].map(showsType))).toEqual([true, false]);
  });

  it('is not when the browser loads neither source', async () => {
    vi.spyOn(HTMLImageElement.prototype, 'currentSrc', 'get').mockReturnValue('');
    vi.spyOn(HTMLImageElement.prototype, 'src', 'set').mockImplementation(function (this: HTMLImageElement) {
      queueMicrotask(() => this.onerror!(new Event('error')));
    });

    expect(await showsType('image/png')).toBe(false);
  });

  it('is asked of the browser once for each type', async () => {
    shows('image/avif');
    const made = vi.spyOn(document, 'createElement');

    expect([await showsType('image/avif'), await showsType('image/avif'), await showsType('image/tiff')]).toEqual([true, true, false]);
    expect(made.mock.calls.filter(([tag]) => tag === 'picture')).toHaveLength(2);
  });
});
