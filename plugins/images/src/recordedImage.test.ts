import { describe, expect, it } from 'vitest';
import { recordedImage } from './recordedImage';

describe('what the reader recorded of an image', () => {
  it('is its size and its placeholder', () => {
    expect(recordedImage({ width: 4032, height: 3024, placeholder: 'data:image/webp;base64,AA' })).toEqual({ width: 4032, height: 3024, placeholder: 'data:image/webp;base64,AA' });
  });

  it.each([null, undefined, {}, { width: 1, height: 1 }, { width: 0, height: 1, placeholder: 'data:,' }, { width: '1', height: 1, placeholder: 'data:,' }])('is nothing in %o', (document) => {
    expect(recordedImage(document)).toBeNull();
  });
});
