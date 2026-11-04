/**
 * The card's size × padding scale, and the one helper that reads off it. Separate from `Card.tsx`
 * because a file that exports components may export nothing else (`react-refresh/only-export-components`),
 * and `cardBleedX` is a plain function callers need — same split as `buttonStyles.ts` / `Button.tsx`.
 */

import { ROUNDED_PX } from './squircleConstants';

/** Size preset controlling a card's border radius and padding scale. */
export type CardSize = 'sm' | 'md' | 'lg';
export type CardPadding = 'none' | 'sm' | 'md' | 'lg';

// The card's own defaults, named so `cardBleedX` can default identically without a second copy of
// them — a caller that omits a prop on `<Card>` must be able to omit it there too and still get the
// matching pair.
export const DEFAULT_CARD_SIZE: CardSize = 'lg';
export const DEFAULT_CARD_PADDING: CardPadding = 'md';

/**
 * One row per size × padding preset: what the card pads itself by, and the classes a child applies
 * to bleed back out to the card's horizontal edges (see {@link cardBleedX}).
 *
 * The two live in the same object because they are one number said twice — a child bleeding with
 * `-mx-5` is only correct while this card pads by `p-5`, and a caller spelling that negative margin
 * at its own call site is holding a copy of a value it cannot see change. Tailwind's scanner needs
 * literal class names, so the bleed cannot be computed from the padding; keeping them on one line
 * is what makes editing one without the other look like the mistake it is.
 */
export const cardPaddingStyles: Record<CardSize, Record<CardPadding, { padding: string; bleedX: string }>> = {
  sm: {
    none: { padding: '', bleedX: '' },
    sm: { padding: 'px-2 py-1', bleedX: '-mx-2 px-2' },
    md: { padding: 'px-2 py-1.5', bleedX: '-mx-2 px-2' },
    lg: { padding: 'px-2.5 py-2', bleedX: '-mx-2.5 px-2.5' },
  },
  md: {
    none: { padding: '', bleedX: '' },
    sm: { padding: 'p-1.5', bleedX: '-mx-1.5 px-1.5' },
    md: { padding: 'p-3', bleedX: '-mx-3 px-3' },
    lg: { padding: 'p-5', bleedX: '-mx-5 px-5' },
  },
  lg: {
    none: { padding: '', bleedX: '' },
    sm: { padding: 'p-3', bleedX: '-mx-3 px-3' },
    md: { padding: 'p-5', bleedX: '-mx-5 px-5' },
    lg: { padding: 'p-7', bleedX: '-mx-7 px-7' },
  },
};

/**
 * The px corner radius a card of each size paints its squircle silhouette at, matching the
 * `rounded-*` token the same size would otherwise carry (lg → `rounded-xl`, sm/md → `rounded-lg`).
 *
 * **Exported because a consumer clipping something to a card's silhouette has to cut the same
 * curve.** `Card` paints its silhouette behind its content and clips nothing, so an element that
 * must reach the card's edges — a full-bleed image — clips itself with `useSquircleClipPath`.
 * Given only `ROUNDED_PX`, such a caller has to re-derive this size-to-radius mapping by hand and
 * hold it in a comment; the two then drift the first time a size's radius moves, and the failure is
 * a clip cutting a different curve from the one painted, which nothing reports.
 *
 * Read it through {@link cardSquircleRadius} rather than indexing this directly, so a caller
 * passing no `size` gets the same default `Card` does.
 */
const squircleRadiusBySize: Record<CardSize, number> = {
  sm: ROUNDED_PX.lg,
  md: ROUNDED_PX.lg,
  lg: ROUNDED_PX.xl,
};

/**
 * The radius to clip to for a `Card` of the given size — the companion to {@link cardBleedX}, and
 * defaulting the same way `Card` does, so `cardSquircleRadius()` matches a `<Card>` with no `size`.
 */
export function cardSquircleRadius(size: CardSize = DEFAULT_CARD_SIZE): number {
  return squircleRadiusBySize[size];
}

/**
 * Classes that let a child of a `Card` paint edge to edge horizontally — a status band across the
 * foot, a divider that has to reach the sides — by cancelling the card's own horizontal padding and
 * re-applying it inside. Pass the same `size` / `padding` the card was given; both default the same
 * way `Card` does, so `cardBleedX()` matches a `<Card>` with neither prop set.
 *
 * **Horizontally only, and never down to the bottom edge.** A squircle card paints its silhouette
 * *behind* its content and clips nothing, so a full-strength wash bled to a corner would paint its
 * own square corner over the rounded one, and no `rounded-b-*` reproduces a superellipse. For the
 * same reason a bleeding wash should be an alpha token: an opaque one paints over the side stroke
 * and notches the outline for the band's height.
 */
export function cardBleedX({
  size = DEFAULT_CARD_SIZE,
  padding = DEFAULT_CARD_PADDING,
}: { size?: CardSize; padding?: CardPadding } = {}): string {
  return cardPaddingStyles[size][padding].bleedX;
}
