import { useEffect } from 'react';

/**
 * Keeps the browser from reloading the page at a pull down for as long as what calls it is shown, and leaves the
 * page as it was after. A browser takes that pull while the finger still moves, before what hears its release
 * can tell whether it was a swipe down. Only up and down: a pull to the side is a trackpad's Back.
 */
export function useNoPullToRefresh(): void {
  useEffect(() => {
    const page = document.documentElement.style;
    const before = page.overscrollBehaviorY;
    page.overscrollBehaviorY = 'none';
    return () => {
      page.overscrollBehaviorY = before;
    };
  }, []);
}
