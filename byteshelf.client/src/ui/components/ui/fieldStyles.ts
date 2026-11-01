import { BUTTON_SQUIRCLE_RADIUS, type ButtonRendering } from './buttonStyles';

/**
 * Corner every field draws — the button's, so a field and the button beside it in a form row share
 * one silhouette rather than two that happen to be close.
 */
export const FIELD_SQUIRCLE_RADIUS = BUTTON_SQUIRCLE_RADIUS;

export interface FieldRenderingOptions {
  /** A refused value: the edge turns danger and stays so under focus. */
  error?: boolean;
  /**
   * Draw the focused look now, whatever holds DOM focus. For a trigger whose menu is open — the
   * `Select` field is a `div` and never takes focus itself, so `focus-within` cannot say it is active.
   */
  open?: boolean;
  /**
   * Glow on focus. `Input`'s `default`, `Textarea`'s `default` and the `Select` field glow; `Input`'s
   * `quiet`, `NumberInput` and `FilterChip` ask for none.
   */
  glow?: boolean;
  /** Rest on a card's shadow (`drop-shadow-card-sm`), the focus glow taking its place under focus. */
  raised?: boolean;
  /**
   * The whole field is something to press (`FilterChip`): under the pointer the layer's own fill
   * steps to `row-hover`, as a secondary button's does, so the hover never covers the rounded edge.
   */
  pressable?: boolean;
}

/** A field's box classes and the {@link Squircle} layer that paints its surface. */
export interface FieldRendering {
  /**
   * The visible box: stacking context for the layer, the transparent 1px border kept for size parity
   * with a plain bordered sibling (the layer covers it via `outsetPx`), and the named group the
   * layer's focus and hover states read.
   */
  className: string;
  squircle: NonNullable<ButtonRendering['squircle']>;
}

/**
 * The one surface every app field paints — `Input` (but `landing`), `Textarea` (`default`), the
 * `Select` field, `NumberInput` and `FilterChip` — so they cannot drift apart into hand-written copies.
 *
 * It is the button's squircle (same corner, same {@link Squircle} layer), for the reason
 * `SquircleSystem.md` gives for
 * painting rather than clipping: a field's focus ring and error edge have to follow the silhouette,
 * and a CSS border or `box-shadow` is locked to the rectangle. So the edge is a stroke on the layer
 * and the focus glow a `drop-shadow` on it, both driven off the box's own `group/field` — named, like
 * `group/btn`, so a field inside some unrelated `group` does not light up with it.
 */
export function fieldRendering({
  error = false,
  open = false,
  glow = true,
  raised = false,
  pressable = false,
}: FieldRenderingOptions = {}): FieldRendering {
  const edge = error
    ? 'stroke-danger-7'
    : open
      ? 'stroke-neutral-9/50'
      : 'stroke-border group-focus-within/field:stroke-neutral-9/50';
  const resting = raised ? 'drop-shadow-card-sm' : undefined;
  const shadow = !glow
    ? resting
    : open
      ? 'drop-shadow-focus-glow'
      : `${resting ?? ''} group-focus-within/field:drop-shadow-focus-glow`.trim();
  return {
    className: 'relative isolate group/field border border-transparent',
    squircle: {
      cornerRadius: FIELD_SQUIRCLE_RADIUS,
      fillClassName: pressable
        ? 'fill-field group-hover/field:fill-row-hover transition-colors duration-200'
        : 'fill-field',
      borderClassName: `${edge} transition-colors duration-200`,
      shadowClassName: shadow,
      outsetPx: 1,
    },
  };
}
