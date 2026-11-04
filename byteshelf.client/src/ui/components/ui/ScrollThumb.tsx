/**
 * The pill painted inside a scroll handle — the visible part of every overlay scrollbar.
 *
 * `CustomScroll`, `OverflowScroll` and `ScrollbarOverlay` each position and size a handle of their
 * own (see `hooks/scrollOverlay.ts`); the pill carries no geometry, only the shared look, so the
 * three render it from this one definition. A copy per consumer would let a retune reach one
 * scrollbar and leave the others behind. The pill is shared as a component because it is markup; the
 * track and handle rules it sits inside are shared in `styles.css`.
 *
 * Internal to the package: a pill outside a scroll handle has no size to fill, so it is deliberately
 * absent from the `@ordo/ui` barrel. Export it when a consumer outside the scroll overlays appears.
 */
export function ScrollThumb() {
  return <div className="relative w-full h-full rounded-full bg-fg/20 border border-fg/10" />;
}
