import { describe, expect, it } from 'vitest';
import type { ShelfFile } from '../../services/shelfService';
import { neighboursOf } from './neighbours';

const files = ['a', 'b', 'c'].map((id) => ({ id }) as ShelfFile);

describe('where a previewed file stands', () => {
  it.each([
    ['a', 1, undefined, 'b'],
    ['b', 2, 'a', 'c'],
    ['c', 3, 'b', undefined],
  ])('finds %s at %i, between %s and %s', (id, position, previous, next) => {
    const found = neighboursOf(files, id);

    expect([found?.position, found?.count, found?.previous?.id, found?.next?.id]).toEqual([position, 3, previous, next]);
  });

  it('finds nothing for a file that is not among them', () => {
    expect(neighboursOf(files, 'd')).toBeNull();
  });
});
