import { afterEach, describe, expect, it, vi } from 'vitest';
import { showsType } from './showsType';

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
});
