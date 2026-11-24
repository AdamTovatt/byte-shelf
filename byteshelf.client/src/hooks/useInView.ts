import { useEffect, useState, type RefObject } from 'react';

/** How far outside what is shown an element counts as in view, so it is there by the time it is scrolled to. */
const MARGIN = '200px';

/** Whether the element has come into view, or close to it, in what scrolls it; it stays so. */
export function useInView(element: RefObject<Element | null>): boolean {
  const [inView, setInView] = useState(false);

  useEffect(() => {
    if (inView || !element.current) return;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) setInView(true);
    }, { root: scrollerOf(element.current), rootMargin: MARGIN });
    observer.observe(element.current);
    return () => observer.disconnect();
  }, [element, inView]);

  return inView;
}

/** What scrolls the element, whose edges the margin is added to: it widens no scroller between it and the element. Null for the page itself. */
function scrollerOf(element: Element): Element | null {
  for (let ancestor = element.parentElement; ancestor && ancestor !== document.body; ancestor = ancestor.parentElement) {
    if (/^(auto|scroll)$/.test(getComputedStyle(ancestor).overflowY)) return ancestor;
  }
  return null;
}
