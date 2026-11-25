import { vi } from 'vitest';

/** An observer the page made, what it was made with and what it watches. */
export interface Watching {
  /** Says whether what it watches is in view, as the browser does when that changes. */
  see: (isIntersecting: boolean) => void;
  options: IntersectionObserverInit | undefined;
  observed: Element[];
  isDisconnected: boolean;
}

/**
 * Stands in for the browser's `IntersectionObserver` until `vi.unstubAllGlobals`: what is observed is in view
 * once `see` says so or, with `isInView`, at once. Answers the observers made, in order.
 */
export function stubIntersectionObserver({ isInView = false } = {}): Watching[] {
  const watching: Watching[] = [];
  vi.stubGlobal('IntersectionObserver', class {
    private readonly mine: Watching;

    constructor(seen: (entries: { isIntersecting: boolean }[]) => void, options?: IntersectionObserverInit) {
      this.mine = { see: (isIntersecting) => seen([{ isIntersecting }]), options, observed: [], isDisconnected: false };
      watching.push(this.mine);
    }

    observe(element: Element) {
      this.mine.observed.push(element);
      if (isInView) this.mine.see(true);
    }

    disconnect() {
      this.mine.isDisconnected = true;
    }
  });
  return watching;
}
