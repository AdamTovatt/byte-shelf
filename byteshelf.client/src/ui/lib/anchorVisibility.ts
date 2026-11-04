/**
 * The `overflow` values that cut a box's content off at its edge. Named rather than read as "not
 * `visible`", which is what a browser computes for the default but not what happy-dom does: it
 * reports an unset `overflow` as an empty string, and every ancestor in every component test would
 * then count as a clip.
 */
const CLIPPING_OVERFLOW: ReadonlySet<string> = new Set(['hidden', 'clip', 'auto', 'scroll']);

/**
 * Whether any part of `anchor` can be seen: what is left of `rect` (its border box, measured by the
 * caller) once it is clipped to the viewport and to every ancestor that clips its overflow.
 *
 * <b>The ancestors, not only the viewport.</b> A row in a scrolling list (`FileList`,
 * `ChatSessionList`) can be pushed past its list's edge while still well inside the viewport, and it
 * is hidden there just as surely as below the fold. An overlay that asked only the viewport would
 * follow that row and stay pinned to something nobody can see — the bug #1837 fixed for a scroll.
 *
 * <b>Any part, not all of it.</b> An overlay beside a half-visible anchor is still beside the thing it
 * belongs to. Only an anchor with nothing left in view has nothing for an overlay to sit beside. An
 * anchor that stopped being laid out (`display: none`) measures as an empty rect at the origin and is
 * not visible either.
 *
 * Clipping is taken per axis, from the computed `overflow-x` / `overflow-y`, so a box that scrolls
 * one way only cuts only that way. Two simplifications, both safe for an anchor that is a control in
 * normal flow, which every caller's is: an ancestor's border box stands in for its padding box (the
 * difference is a border and a scrollbar), and an absolutely positioned anchor escaping a
 * non-positioned ancestor's clip is not modelled. `<body>` and `<html>` are skipped, because their
 * overflow is applied to the viewport rather than to their own boxes, and the viewport is already
 * the first clip.
 *
 * Reads a computed style per ancestor, so a caller runs it when it has a reason to — Popover asks only
 * once `anchorMoved` says the anchor is somewhere new — rather than on every frame.
 *
 * Origin: #2515.
 */
export function anchorVisible(anchor: Element, rect: DOMRect): boolean {
  let top = Math.max(rect.top, 0);
  let bottom = Math.min(rect.bottom, window.innerHeight);
  let left = Math.max(rect.left, 0);
  let right = Math.min(rect.right, window.innerWidth);

  for (
    let ancestor = anchor.parentElement;
    ancestor && ancestor !== document.body && ancestor !== document.documentElement;
    ancestor = ancestor.parentElement
  ) {
    if (top >= bottom || left >= right) return false;

    const style = window.getComputedStyle(ancestor);
    const clipsX = CLIPPING_OVERFLOW.has(style.overflowX);
    const clipsY = CLIPPING_OVERFLOW.has(style.overflowY);
    if (!clipsX && !clipsY) continue;

    const box = ancestor.getBoundingClientRect();
    if (clipsY) {
      top = Math.max(top, box.top);
      bottom = Math.min(bottom, box.bottom);
    }
    if (clipsX) {
      left = Math.max(left, box.left);
      right = Math.min(right, box.right);
    }
  }

  return top < bottom && left < right;
}
