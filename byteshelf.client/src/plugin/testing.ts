// What plugins' tests import as `@byteshelf/plugin/testing`: stand-ins for what the app gives a viewer, so that one
// change to what it gives is one change here. Only tests resolve it (build/pluginTests.config.ts); a plugin's own
// module that imports it fails its build, as any other module of `@byteshelf/plugin` does.
import type { DerivedKind, DerivedReader } from './index';

/** What plugins made from a file: each kind in `made` is listed and loads from its URL, and there is none of any other. */
export function derivedReaderOf(made: Partial<Record<DerivedKind, string>> = {}): DerivedReader {
  return {
    has: (kind) => made[kind] !== undefined,
    url: async (kind) => made[kind] ?? null,
    forget: () => {},
    status: async (kind) => (made[kind] !== undefined ? 'made' : 'none'),
  };
}
