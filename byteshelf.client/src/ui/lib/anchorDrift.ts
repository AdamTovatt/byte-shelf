/**
 * How far an anchor may drift, in CSS pixels, before a scroll counts as having moved it.
 *
 * Not a fudge factor for a wrong comparison: a browser reports the *unmoved* case as an exact zero
 * delta, and this absorbs sub-pixel noise from fractional scroll offsets and non-integer device pixel
 * ratios. Both directions of error are bounded — an anchor that moved less than this stays anchored
 * only until the next scroll event, and one that jittered under it keeps an overlay that is still
 * painted where it belongs.
 */
export const ANCHOR_DRIFT_TOLERANCE_PX = 0.5;

/** Whether two edge positions differ by more than the tolerance above. */
function drifted(edge: number, wasAt: number): boolean {
  return Math.abs(edge - wasAt) > ANCHOR_DRIFT_TOLERANCE_PX;
}

/**
 * Whether `rect` sits anywhere other than where an overlay was last positioned against.
 *
 * **This, not a `scroll` event, is the detachment condition.** An overlay portaled to the body as
 * `position: fixed` does not travel with a scrolled region, so it has to be taken away once its
 * anchor is somewhere else. A `scroll` event does not say that happened: the browser fires one for
 * scrolls nobody asked for — scroll anchoring shifting `scrollTop` to hold visible content still, or
 * a scroll in a container the overlay has nothing to do with — and closing on those takes an overlay
 * away from a user who did nothing to it. Comparing rects asks the question the overlay actually has.
 *
 * A caller still owns two decisions this does not make, and both belong at the call site because both
 * turn on properties of the overlay:
 *
 * - **What to do with no baseline to compare against.** The two callers it was extracted for answered
 *   this oppositely on purpose. `Popover` closes: its open state is independent of any measurement, so a
 *   panel it cannot vouch for should go. The docx slot-creation prompt returned early: the pinned block
 *   *was* its existence signal, so no baseline meant there was no overlay to dismiss. That prompt went
 *   with the old template page (#2838), leaving `Popover` the one caller; the second answer stays here
 *   for the next overlay whose existence is its anchor.
 * - **Whether to follow the anchor instead of dismissing.** That turns on what the overlay is painted
 *   from, and on what would hide it once the anchor scrolls out of its region.
 *
 * One edge per line through {@link drifted}, rather than four spellings of the same comparison: the
 * repeated form is where a `top` compared against a `bottom` hides, and a test that moves a whole
 * anchor cannot catch that, since a moving anchor moves every edge together.
 *
 * Origin: #1837 (`Popover`), extracted on its second consumer in #1840, the docx slot-creation prompt,
 * which #2838 removed.
 */
export function anchorMoved(rect: DOMRect, positionedAgainst: DOMRect): boolean {
  return (
    drifted(rect.top, positionedAgainst.top) ||
    drifted(rect.bottom, positionedAgainst.bottom) ||
    drifted(rect.left, positionedAgainst.left) ||
    drifted(rect.right, positionedAgainst.right)
  );
}
