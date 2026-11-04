import { useCallback, useMemo, useSyncExternalStore } from 'react';

/**
 * Returns whether the given CSS media query currently matches, re-rendering on changes.
 *
 * **Safe to render on a server, which is not decoration.** `window` does not exist under
 * `react-dom/server`, and reading it there is a `ReferenceError` thrown from inside whichever
 * component called the hook — a build failure for a consumer that prerenders, not a test failure.
 * `ordo.marketing` prerenders every shared component it uses, which is how this surfaced (#2312).
 *
 * Fixed here rather than forked by the consumer, on the same ground as `Logo`'s missing-provider
 * defect: the copy-on-change rule is for a site wanting a component to behave DIFFERENTLY, and this
 * is a component that does not work outside a browser. In a browser nothing changes — `window` is
 * defined, so every path below is the one it always took.
 *
 * The server snapshot is `false` for every query, which is a real decision rather than a neutral
 * default: it means a prerendered document is the one a narrow viewport would get, and a wide client
 * corrects it on the first commit after hydration. `false` is the safer of the two because the
 * arrangements these queries gate degrade that way round — a narrow arrangement is legible at any
 * width, where a wide one is not.
 */
export function useMediaQuery(query: string): boolean {
  const mediaQueryList = useMemo(
    () => (typeof window === 'undefined' ? undefined : window.matchMedia(query)),
    [query],
  );

  const subscribe = useCallback((onChange: () => void) => {
    if (mediaQueryList === undefined) return () => {};

    mediaQueryList.addEventListener('change', onChange);
    return () => mediaQueryList.removeEventListener('change', onChange);
  }, [mediaQueryList]);

  return useSyncExternalStore(subscribe, () => mediaQueryList?.matches ?? false, () => false);
}
