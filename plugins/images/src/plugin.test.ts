import { expect, it } from 'vitest';
import plugin from './plugin';

it('shows images, and no file stored without a type', () => {
  expect(plugin.viewers!.map(({ mediaTypes, extensions }) => [mediaTypes, extensions])).toEqual([[['image/*'], []]]);
});
