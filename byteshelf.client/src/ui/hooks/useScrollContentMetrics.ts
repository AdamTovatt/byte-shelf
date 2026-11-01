import { type RefObject, useEffect, useRef, useState } from 'react';

/** A scroll element's viewport size measured against its content size, on both axes. */
export interface ScrollContentMetrics {
  viewWidth: number;
  viewHeight: number;
  contentWidth: number;
  contentHeight: number;
}

export interface UseScrollContentMetricsResult {
  /**
   * The latest measurements. A ref rather than state, so a non-reactive reader — a scroll listener, a
   * drag handler — can read the current values without re-subscribing on every resize.
   *
   * Read-only to consumers: the hook compares these against each new measurement to decide whether
   * anything changed, so a consumer writing to them would corrupt that.
   */
  metricsRef: RefObject<Readonly<ScrollContentMetrics>>;
  hasHorizontalOverflow: boolean;
  hasVerticalOverflow: boolean;
  /**
   * Increments on every measurement. A consumer with geometry derived from {@link metricsRef} — which
   * mutates in place and so cannot be a dependency — depends on this to recompute at the right time.
   */
  measurementVersion: number;
}

/**
 * Measures a scroll element against its content and reports whether it overflows on each axis,
 * re-measuring whenever the element or anything inside it resizes, is added or removed, or changes an
 * attribute.
 *
 * Two kinds of consumer share this, and they have to agree: `useScrollbarOverlay`, which renders a
 * track per overflowing axis, and any consumer that must leave room for those tracks — an overlay
 * scrollbar floats above the content instead of taking layout space, so whatever pads for it needs the
 * same answer to "is that track on screen?". What this shares is the FORMULA, so the two can't drift
 * apart on what counts as overflow. It does not share the observer: a component that both renders
 * `ScrollbarOverlay` and pads for it calls this twice and gets two ResizeObservers on one element.
 * That is the accepted price of leaving `ScrollbarOverlay` a two-prop catalog component rather than
 * threading measurements through it for every consumer in the app.
 *
 * <b>Every element inside it is watched, not only its first child</b> (#2514). Content overflows a
 * scroll element from wherever it is wider or taller than the space left for it, and that is often
 * not the content wrapper: a landscape sheet runs past a page column whose own box never changes, so
 * a reading taken before the sheet had its width stayed the answer until something else happened to
 * resize. The report document's sideways bar was drawn on some runs and not others over the same
 * layout. So a resize anywhere in the subtree re-measures, elements added later are watched as they
 * arrive, and a change to any element's attributes re-measures too, since a style or class can move
 * content past the edge without resizing anything.
 *
 * <b>What that costs was measured, not assumed, and it is the size watching that costs.</b> On a
 * report document of 734 elements, ten extra copies of these two observers added about 85ms of
 * main-thread time to 88 keystrokes of typing, inside the 225ms the same typing varied by between runs
 * with none. On the file preview grid with 400 rows loaded, 7,235 elements, a column-resize drag
 * already spent about 160ms a step; ten extra copies of the size observer alone added about 5ms a step
 * each, and ten of the mutation observer alone added nothing the runs could tell apart. So the price
 * is paid where a scroller holds thousands of elements and something re-lays them out every frame,
 * and it is paid for the property this hook exists for: a size change anywhere inside is seen. An
 * attribute filter would save nothing measurable, and would be a rule about which attributes can move
 * layout, kept nowhere a stylesheet author would see it.
 */
export function useScrollContentMetrics(scrollRef: RefObject<HTMLElement | null>): UseScrollContentMetricsResult {
  const metricsRef = useRef<ScrollContentMetrics>({
    viewWidth: 0,
    viewHeight: 0,
    contentWidth: 0,
    contentHeight: 0,
  });
  const [hasHorizontalOverflow, setHasHorizontalOverflow] = useState(false);
  const [hasVerticalOverflow, setHasVerticalOverflow] = useState(false);
  const [measurementVersion, setMeasurementVersion] = useState(0);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;

    const measure = () => {
      const metrics = metricsRef.current;
      const viewWidth = el.clientWidth;
      const viewHeight = el.clientHeight;
      const contentWidth = el.scrollWidth;
      const contentHeight = el.scrollHeight;

      // A ResizeObserver fires for changes on either axis and for a parent's reflow, so most callbacks
      // find the same numbers as last time. Bumping the version regardless would re-render every
      // consumer on each of those — including grids whose own observers correctly bail out — so the
      // version moves only when a measurement actually did.
      if (
        viewWidth === metrics.viewWidth
        && viewHeight === metrics.viewHeight
        && contentWidth === metrics.contentWidth
        && contentHeight === metrics.contentHeight
      ) {
        return;
      }

      metrics.viewWidth = viewWidth;
      metrics.viewHeight = viewHeight;
      metrics.contentWidth = contentWidth;
      metrics.contentHeight = contentHeight;

      setHasHorizontalOverflow(contentWidth > viewWidth);
      setHasVerticalOverflow(contentHeight > viewHeight);
      setMeasurementVersion((version) => version + 1);
    };

    measure();

    const resizeObserver = new ResizeObserver(measure);
    const eachInSubtree = (root: Element, visit: (element: Element) => void) => {
      visit(root);
      for (const descendant of root.querySelectorAll('*')) visit(descendant);
    };
    const watch = (element: Element) => resizeObserver.observe(element);
    const unwatch = (element: Element) => resizeObserver.unobserve(element);
    eachInSubtree(el, watch);

    // Every removal is let go of before anything is watched, and an added node is watched only if it is
    // still inside the scroll element NOW. A batch reports what happened in order, not where things
    // ended up: a node moved within the scroller is reported removed and then added, and must end
    // watched; a node added to a subtree the same batch removed is reported added, and must not, or it
    // stays watched outside the scroller until unmount.
    const mutationObserver = new MutationObserver((records) => {
      for (const record of records) {
        for (const node of record.removedNodes) if (node instanceof Element) eachInSubtree(node, unwatch);
      }
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (node instanceof Element && el.contains(node)) eachInSubtree(node, watch);
        }
      }
      measure();
    });
    mutationObserver.observe(el, { childList: true, attributes: true, subtree: true });

    return () => {
      mutationObserver.disconnect();
      resizeObserver.disconnect();
    };
  }, [scrollRef]);

  return { metricsRef, hasHorizontalOverflow, hasVerticalOverflow, measurementVersion };
}
