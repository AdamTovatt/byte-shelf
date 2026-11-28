// What plugins' tests import as `@byteshelf/plugin/testing`: stand-ins for what the app and the browser give a viewer,
// so that one change to what they give is one change here. Only tests resolve it (build/pluginTests.config.ts); a
// plugin's own module that imports it fails its build, as any other module of `@byteshelf/plugin` does. The app's own
// tests use it too.
import type { DerivedKind, DerivedReader } from './index';

export { InPreview } from './InPreview';

/**
 * What plugins made from a file: each kind in `made` is listed and loads from its URL, and there is none of any
 * other; with what `overrides` says instead, as a status a test asks for.
 */
export function derivedReaderOf(made: Partial<Record<DerivedKind, string>> = {}, overrides: Partial<DerivedReader> = {}): DerivedReader {
  return {
    has: (kind) => made[kind] !== undefined,
    url: async (kind) => made[kind] ?? null,
    forget: () => {},
    status: async (kind) => (made[kind] !== undefined ? 'made' : 'none'),
    ...overrides,
  };
}

/** A browser that makes elements full screen, which happy-dom is not: each element asked to go full screen, and how often it was left. */
export interface FullScreenStub {
  requested: Element[];
  exits: number;
  /** Makes `element` the one full screen, as the browser says. */
  show: (element: Element | null) => void;
  restore: () => void;
}

/**
 * Makes the browser one that makes elements full screen, or with `refuses` one that refuses each asking, going there
 * and back, as a browser does without a click to allow it; undo with `restore`.
 */
export function stubFullScreen({ refuses = false }: { refuses?: boolean } = {}): FullScreenStub {
  let shown: Element | null = null;
  const request = Object.getOwnPropertyDescriptor(Element.prototype, 'requestFullscreen');
  const stub: FullScreenStub = {
    requested: [],
    exits: 0,
    show: (element) => {
      shown = element;
      document.dispatchEvent(new Event('fullscreenchange'));
    },
    restore: () => {
      for (const name of ['fullscreenEnabled', 'fullscreenElement', 'exitFullscreen']) Reflect.deleteProperty(document, name);
      if (request) Object.defineProperty(Element.prototype, 'requestFullscreen', request);
      else Reflect.deleteProperty(Element.prototype, 'requestFullscreen');
    },
  };
  Object.defineProperties(document, {
    fullscreenEnabled: { value: true, configurable: true },
    fullscreenElement: { get: () => shown, configurable: true },
    exitFullscreen: {
      value: async () => {
        stub.exits++;
        if (refuses) throw new TypeError('Refused.');
        stub.show(null);
      },
      configurable: true,
    },
  });
  Object.defineProperty(Element.prototype, 'requestFullscreen', {
    value: async function (this: Element) {
      stub.requested.push(this);
      if (refuses) throw new TypeError('Refused.');
      stub.show(this);
    },
    configurable: true,
  });
  return stub;
}
