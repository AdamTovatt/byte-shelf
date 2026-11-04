import type { ReactNode } from 'react';

export interface HasContentOptions {
  /**
   * Counts `''` as nothing too, for a slot whose content is words: an empty message would draw a
   * line holding nothing. Off by default, where `''` is a caller's own business.
   */
  ignoreEmptyString?: boolean;
}

/**
 * Whether an optional `ReactNode` prop holds something to draw — not `undefined`, `null` or `false`,
 * the three a caller's `{condition && node}` or an unset prop hands over. One spelling, so the
 * components that place a slot only when it is filled cannot disagree about what "filled" means.
 */
export function hasContent(node: ReactNode, { ignoreEmptyString = false }: HasContentOptions = {}): boolean {
  if (node === undefined || node === null || node === false) return false;
  return !(ignoreEmptyString && node === '');
}
