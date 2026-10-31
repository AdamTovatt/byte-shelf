import { describe, expect, it } from 'vitest';
import { colors } from './colors';

/** Every colour in the palette with where it sits, such as "brand.primary.light". */
function leaves(value: unknown, at: string): [string, unknown][] {
  if (typeof value !== 'object' || value === null) return [[at, value]];
  return Object.entries(value).flatMap(([key, nested]) => leaves(nested, at ? `${at}.${key}` : key));
}

describe('colors', () => {
  it('gives every colour as a hex or rgba value Tailwind and CSS read', () => {
    const invalid = leaves(colors, '').filter(([, value]) => typeof value !== 'string' || !/^(#[0-9a-fA-F]{6}|rgba\(\d+, \d+, \d+, (0|1|0?\.\d+)\))$/.test(value));

    expect(invalid).toEqual([]);
  });

  it('gives every colour for both themes', () => {
    const groups = leaves(colors, '').map(([at]) => at.slice(0, at.lastIndexOf('.')));
    const themes = (group: string) => leaves(colors, '').filter(([at]) => at.startsWith(`${group}.`)).map(([at]) => at.slice(group.length + 1)).sort();

    for (const group of new Set(groups)) {
      expect(themes(group), group).toEqual(['dark', 'light']);
    }
  });
});
