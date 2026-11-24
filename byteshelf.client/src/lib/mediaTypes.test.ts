import { describe, expect, it } from 'vitest';
import { charsetOf } from './mediaTypes';

describe('the charset of a stored type', () => {
  it.each([
    ['text/plain; charset=iso-8859-1', 'iso-8859-1'],
    ['text/plain;format=flowed;Charset="windows-1252"', 'windows-1252'],
    ['text/plain', undefined],
    ['text/x-charset=utf-16', undefined],
  ])('in %s is %s', (contentType, charset) => {
    expect(charsetOf(contentType)).toBe(charset);
  });
});
