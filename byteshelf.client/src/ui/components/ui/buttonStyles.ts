import type { IconButtonVariant } from './IconButton';
import { ROUNDED_PX } from './squircleConstants';
import { LINK_UNDERLINE_CLASS } from './linkUnderline';

/**
 * Two looks carry a view's actions (#2455): `main`, the near-black fill for the ONE main action of a
 * form, card or modal, and `secondary`, the field-white button for everything else. The rest are not
 * a third look but other kinds of control — a destructive confirm, a toolbar ghost, an inline link —
 * plus the public page's own pair.
 */
export type ButtonVariant =
  | 'main'
  | 'secondary'
  | 'danger'
  | 'ghost'
  | 'outlineDanger'
  | 'link'
  | 'text'
  | 'inlineLink'
  | 'mutedInlineLink';
export type ButtonSize = '2xs' | 'xs' | 'sm' | 'md' | 'lg' | 'xl';
/**
 * The button's silhouette. `default` is the size token's corner (smoothed into a squircle for the
 * filled variants); `pill` is a fully-round end cap; `chip` is the tighter corner a control sitting
 * inside a dense row wants.
 *
 * A named shape rather than a `rounded-*` class on `className`: the squircle silhouette is painted at
 * one radius and cannot follow a caller's, so the component has to *know* a pill was asked for in
 * order to drop back to plain `border-radius`. Reading that intent out of a class string would make
 * an unrelated utility able to change the rendered structure.
 *
 * **`chip` is why that matters twice over.** The default corner is `rounded-lg` (12px) at
 * every size, which on a ~25px-tall `size="xs"` button is half the height — so the control renders
 * as a capsule, beside rows and pills drawn at 6–8px. A class cannot fix that on a filled variant:
 * {@link squircleButtonClassName} strips the size token's corner and a {@link Squircle} paints
 * {@link BUTTON_SQUIRCLE_RADIUS} behind a transparent background, so a `rounded-*` at the call site
 * has no surface to clip and changes nothing on screen. `chip` moves the radius the silhouette is
 * painted at, which is the only place it can be changed from.
 */
export type ButtonShape = 'default' | 'pill' | 'chip';

/**
 * The secondary button's plain-path surface — fill, edge colour and hover — shared with
 * `IconButton`'s `secondary` so the two cannot drift apart. Carries no border WIDTH: `buttonClassName`
 * adds the `border` per path, and `IconButton` adds its own. A disabled button keeps its resting fill
 * under the pointer (`disabled:hover:`): a browser still matches `:hover` on a disabled button.
 */
export const SECONDARY_SURFACE_CLASS = 'bg-field border-border hover:bg-row-hover disabled:hover:bg-field';

/** `main`'s plain-path surface, shared with `IconButton`'s `main` on the same terms as the one above. */
export const MAIN_SURFACE_CLASS = 'bg-btn-main border-transparent hover:bg-btn-main/90 disabled:hover:bg-btn-main';

/** The `IconButton` variants drawn on a squircle silhouette (the filled ones, the two looks). */
export type SquircleIconButtonVariant = 'main' | 'secondary';

/** Whether an `IconButton` variant is one {@link squircleIconButtonClassName} draws. */
export function isSquircleIconButtonVariant(variant: IconButtonVariant): variant is SquircleIconButtonVariant {
  return variant === 'main' || variant === 'secondary';
}

// Layout / typography / focus — everything except the 1px `border` (added per-path below so the
// squircle path and the inline variants can omit it). The focus ring lives here and, crucially, stays on the real button box
// in squircle mode too: the Squircle fill is a behind-content layer that clips nothing, so the ring
// is not lost the way a mask/clip-path would lose it.
/**
 * The keyboard focus ring every button draws. Exported for a control that is a button without being
 * a `Button` so it shows focus the same way rather than
 * spelling the ring again.
 */
export const FOCUS_RING_CLASS =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-fg focus-visible:ring-offset-2 focus-visible:ring-offset-bg';

const baseLayout = `inline-flex items-center justify-center gap-2.5 font-medium transition-elegant ${FOCUS_RING_CLASS}`;

/** What the inline variants share: no box of their own, and the size, leading and tracking of the line they sit in. */
const THE_LINES_METRICS = 'bg-transparent !p-0 !rounded-none !text-[length:inherit] !leading-[inherit] !tracking-[inherit]';

/** What `text` and `inlineLink` share: {@link THE_LINES_METRICS}, and the line's colour too. */
const INHERITS_THE_LINE = `${THE_LINES_METRICS} text-inherit`;

const variantStyles: Record<ButtonVariant, string> = {
  /**
   * The view's one main action: filled with the text colour and lettered in `panel`, so it is
   * near-black on white in light and inverts in dark. At most one per form, card or modal — a second
   * would make the reader choose between two "main" things, which is what the look exists to spare.
   */
  main: `${MAIN_SURFACE_CLASS} text-on-btn-main disabled:opacity-40 disabled:cursor-not-allowed`,
  /**
   * The quiet button, drawn as a FIELD: the `field` fill (`panel`, the standard white) inside the
   * field's grey hairline, so a secondary button and the input beside it read as one kind of object.
   * On a card it sits level with the card and is defined by its edge —
   * which is also what makes it the variant for a control that should read as an area of the page you
   * can press. It looks the same on a card as on the canvas, as `main` does: nothing brightens.
   */
  secondary: `${SECONDARY_SURFACE_CLASS} text-fg disabled:opacity-40 disabled:cursor-not-allowed`,
  danger: 'bg-danger-a-3 text-danger-11 border-danger-a-6 hover:bg-danger-a-4 hover:border-danger-a-7 disabled:opacity-40 disabled:cursor-not-allowed',
  ghost: 'bg-transparent text-fg border-transparent hover:bg-surface disabled:opacity-40 disabled:cursor-not-allowed',
  outlineDanger: 'bg-transparent text-danger-11 border border-danger-a-6 hover:border-danger-a-8 hover:bg-danger-a-2 disabled:opacity-40 disabled:cursor-not-allowed',
  link: 'bg-transparent text-fg !p-0 !rounded-none underline disabled:opacity-40 disabled:cursor-not-allowed',
  text: `${INHERITS_THE_LINE} disabled:opacity-40 disabled:cursor-not-allowed`,
  /**
   * A link inside a line of text that acts rather than navigates (opens a panel, selects a box,
   * reads more): `text`'s inheritance of the line's size, leading and tracking, its weight and its
   * colour too, so it is lettered exactly as the words around it, underlined the way `InternalLink`
   * underlines (`LINK_UNDERLINE_CLASS`). One variant rather than `text` or `link` with the missing
   * inheritance forced at each call site, which is how three copies of it came to differ (#2807).
   */
  inlineLink: `${INHERITS_THE_LINE} ![font-weight:inherit] ${LINK_UNDERLINE_CLASS} disabled:opacity-40 disabled:cursor-not-allowed`,
  /**
   * `inlineLink` in grey that turns to the text colour under the pointer: the quiet way further in, set under the words
   * it belongs to (#2941's "Show details" and source lines). Its own variant rather than `inlineLink` with a colour on
   * `className`, which won only by the stylesheet's order over the `text-inherit` it carries.
   */
  mutedInlineLink: `${THE_LINES_METRICS} text-muted hover:text-fg ![font-weight:inherit] ${LINK_UNDERLINE_CLASS} disabled:opacity-40 disabled:cursor-not-allowed`,
};

/** The corner token inside a size step — the one part of it that {@link ButtonShape} replaces. */
const RADIUS_TOKEN = /\brounded-\S+/;

/**
 * What each named shape's corner is, as the Tailwind token the plain path writes and the px a
 * squircle layer is painted at.
 *
 * One table rather than a branch per path, because the two have to agree: a shape whose token said
 * 8px while its silhouette painted 12 would render differently on a `ghost` button and a `secondary`
 * one, and nothing in the DOM of either would say so.
 *
 * `pill`'s radius is `'pill'` rather than a number: it is half the measured height, which no token
 * knows, so the layer that traces it resolves it at render.
 */
const SHAPE_CORNER: Record<Exclude<ButtonShape, 'default'>, { token: string; radius: number | 'pill' }> = {
  pill: { token: 'rounded-full', radius: 'pill' },
  chip: { token: 'rounded-md', radius: ROUNDED_PX.md },
};

const sizeStyles: Record<ButtonSize, string> = {
  /**
   * The step below `xs`, 22px tall: for a canvas's own small toolbar (`PanZoomCanvas`'s Fit beside its
   * 22px `IconButton`s), where the controls sit over or under content and should take as little of it
   * as they can. Not for a page.
   */
  '2xs': 'h-[1.375rem] px-1.5 text-body-xs rounded-lg tracking-wide',
  /**
   * A control sitting INSIDE a dense row rather than standing on its own.
   *
   * `sm` is the smallest button that reads as a button on a page; this is the step below, for a row
   * whose height is set by something else — a table row, a toolbar strip — where the button has to
   * fit the row rather than the row grow to fit it. It is a padding step and nothing else: `px-2
   * py-1` against `sm`'s `px-3 py-1.5`, keeping `sm`'s typography and corner.
   *
   * The corner stays `rounded-lg` deliberately, even in rows drawn with 8px corners; such a control
   * asks for the `chip` {@link ButtonShape}. A size step cannot own its radius here:
   * {@link BUTTON_SQUIRCLE_RADIUS} is one value for every size, and on the squircle path
   * {@link buttonRendering} strips the size token's corner and paints that constant instead — so a
   * step carrying its own radius would render it on the transparent variants and silently not on
   * the filled ones.
   */
  xs: 'px-2 py-1 text-xs leading-[0.95rem] rounded-lg tracking-wide',
  sm: 'px-3 py-1.5 text-xs leading-[0.95rem] rounded-lg tracking-wide',
  md: 'px-4 py-2 text-sm leading-[1.1rem] rounded-lg tracking-wide',
  lg: 'px-6 py-2.5 text-base leading-[1.3rem] rounded-lg tracking-wide',
  xl: 'px-8 py-3.5 text-lg leading-[1.5rem] rounded-lg tracking-wide',
};

/**
 * The corner an `IconButton` draws on the plain path: `rounded-lg` by default, the shape's own token
 * otherwise. Read from {@link SHAPE_CORNER} so an icon button and a text button asked for the same
 * shape cannot disagree about what it is.
 */
export function iconButtonCornerClass(shape: ButtonShape): string {
  return shape === 'default' ? 'rounded-lg' : SHAPE_CORNER[shape].token;
}

/** Swaps the size token's corner for the shape's, so a radius is never spelled out twice. */
function sizeClassesFor(size: ButtonSize, shape: ButtonShape): string {
  return shape === 'default'
    ? sizeStyles[size]
    : sizeStyles[size].replace(RADIUS_TOKEN, SHAPE_CORNER[shape].token);
}

/**
 * The variants that sit in a line of text rather than standing as a box, so they carry NO border.
 * Every other plain-path variant keeps a 1px one — transparent where it draws nothing — so boxes of
 * one size line up whatever their variant. For these two that parity buys nothing and costs the
 * line: `text` takes the surrounding size and line height precisely so it is exactly as tall as the
 * text it sits in, and a transparent border still made it 2px taller.
 */
const INLINE_VARIANTS: ReadonlySet<ButtonVariant> = new Set<ButtonVariant>(['link', 'text', 'inlineLink', 'mutedInlineLink']);

export function buttonClassName(
  variant: ButtonVariant = 'secondary',
  size: ButtonSize = 'md',
  shape: ButtonShape = 'default',
): string {
  const border = INLINE_VARIANTS.has(variant) ? '' : ' border';
  return `${baseLayout}${border} ${variantStyles[variant]} ${sizeClassesFor(size, shape)}`;
}

/** Radius (px) matching the `rounded-lg` token every button size uses. */
export const BUTTON_SQUIRCLE_RADIUS = ROUNDED_PX.lg;

/**
 * Corner a plain (non-squircle) button actually paints, for a layer that has to trace it.
 *
 * Three answers, not one. A pill is fully round and its radius is half the measured height, which no
 * token knows — `'pill'` defers it to the layer. `link` and `text` carry `!rounded-none`, so their box
 * is square and a rounded trace would float off two corners of it; they are the reason this is a
 * function rather than the constant above, which would have been silently wrong for them the first
 * time either one was asked to look busy. Everything else is the size token's `rounded-lg`.
 */
function plainButtonRadius(variant: ButtonVariant, shape: ButtonShape): number | 'pill' {
  if (shape !== 'default') return SHAPE_CORNER[shape].radius;
  return SQUARE_CORNERED_VARIANTS.has(variant) ? 0 : BUTTON_SQUIRCLE_RADIUS;
}

/** Variants whose `variantStyles` entry forces `!rounded-none`. */
const SQUARE_CORNERED_VARIANTS: ReadonlySet<ButtonVariant> = new Set(['link', 'text', 'inlineLink', 'mutedInlineLink']);

// Shared by the button + icon-button squircle boxes: a transparent 1px border for outer-size parity
// with plain (non-squircle) buttons, plus the stacking context + `group/btn` the Squircle fill needs
// to react to hover. The Squircle covers the transparent border via `outsetPx={1}`.
//
// **Named, not bare `group`.** A bare `group` on this box is satisfied by `:hover` on ANY ancestor
// that also carries an unnamed `group` — Tailwind's selector is `.group:hover .group-hover:X`, which
// does not care which `.group` matched. `ListButton`'s row is exactly such an ancestor (its own
// unnamed `group` is what lets a sibling reveal on row-hover, e.g. `ProjectSidebarSection`'s pin
// icon), so a squircle button sitting in one of its rows used to light up its own fill whenever the
// ROW was hovered anywhere — including nowhere near the button (observed on the pipeline hub's Run
// and Open buttons, #2113 follow-up). Naming this button's own group scopes its `group-hover/btn:`
// rules to its own hover, and only its own, however many unrelated `group`s it ends up nested inside.
const squircleBoxBase = 'border border-transparent relative isolate group/btn';

/** One variant's squircle surface: see {@link squircleButtonStyles}. */
interface SquircleButtonStyle {
  box: string;
  fill: string;
  border: string;
  shadow?: string;
  radius?: number;
}

/**
 * Squircle surface definitions for the button variants whose EDGE IS VISIBLE AT REST — which is not
 * the same as "filled": `outlineDanger` paints no fill and is here, because a hairline
 * traces the corner and so the superellipse is what a reader actually sees. In squircle mode a
 * {@link Squircle} layer paints the fill / border / shadow behind the button content (instead of the
 * button's CSS background), which lets the button keep its real box — and therefore its focus ring.
 * `ghost` / `link` / `text` are intentionally absent: they show neither fill nor edge at rest, so
 * their corners are barely visible and they stay on plain `border-radius`.
 *
 * KEEP IN SYNC with `variantStyles` above — `box` carries the non-surface classes (text + disabled),
 * `fill` mirrors the bg states, `border` mirrors the border states, `shadow` re-expresses any
 * box-shadow as a drop-shadow (which follows the squircle silhouette).
 *
 * The two looks are required entries, the rest optional: `IconButton`'s squircled variants read
 * their `box` from here (`SQUIRCLE_ICON_BUTTON_BOX`), so those two are always present.
 */
const squircleButtonStyles: Record<SquircleIconButtonVariant, SquircleButtonStyle> & Partial<Record<ButtonVariant, SquircleButtonStyle>> = {
  main: {
    box: 'text-on-btn-main disabled:opacity-40 disabled:cursor-not-allowed',
    // `group-disabled/btn:` comes after `group-hover/btn:` in Tailwind's variant order, so a disabled
    // button keeps its resting fill under the pointer.
    fill: 'fill-btn-main group-hover/btn:fill-btn-main/90 group-disabled/btn:fill-btn-main transition-colors duration-200',
    border: 'stroke-transparent',
  },
  secondary: {
    box: 'text-fg disabled:opacity-40 disabled:cursor-not-allowed',
    fill: 'fill-field group-hover/btn:fill-row-hover group-disabled/btn:fill-field transition-colors duration-200',
    border: 'stroke-border',
  },
  // Danger keeps its translucent alpha fill (matching the plain button). The clipped-stroke border
  // composites over it correctly — a stacked/opaque approach would either change the colour or let the
  // border bleed through.
  danger: {
    box: 'text-danger-11 disabled:opacity-40 disabled:cursor-not-allowed',
    fill: 'fill-danger-a-3 group-hover/btn:fill-danger-a-4 transition-colors duration-200',
    border: 'stroke-danger-a-6 group-hover/btn:stroke-danger-a-7 transition-colors duration-200',
  },
  // Transparent fill with a persistent (squircle) outline; hover tints the fill and darkens the border.
  outlineDanger: {
    box: 'text-danger-11 disabled:opacity-40 disabled:cursor-not-allowed',
    fill: 'fill-transparent group-hover/btn:fill-danger-a-2 transition-colors duration-200',
    border: 'stroke-danger-a-6 group-hover/btn:stroke-danger-a-8 transition-colors duration-200',
  },
};

/** The ink and disabled treatment of each squircled `IconButton` variant: its text button's own `box`. */
const SQUIRCLE_ICON_BUTTON_BOX: Record<SquircleIconButtonVariant, string> = {
  main: squircleButtonStyles.main.box,
  secondary: squircleButtonStyles.secondary.box,
};

/**
 * Squircle fill / border / shadow classes for a variant whose edge is visible at rest, or null to
 * render the plain rounded button — either because the variant shows no edge at rest (corners barely
 * visible) or because a `pill` was asked for, a shape the fixed silhouette cannot paint.
 *
 * A `chip` keeps the squircle and moves its radius. The shape's corner wins over the variant's own,
 * which is the point of asking for a shape at all: `chip` says "this control sits inside a dense
 * row", and a marketing CTA's deliberately larger corner would contradict it.
 */
export function squircleButtonSurface(
  variant: ButtonVariant,
  shape: ButtonShape = 'default',
): { fill: string; border: string; shadow?: string; radius: number } | null {
  if (shape === 'pill') return null;
  const styles = squircleButtonStyles[variant];
  if (!styles) return null;
  const shapeRadius = shape === 'default' ? null : SHAPE_CORNER[shape].radius;
  return {
    fill: styles.fill,
    border: styles.border,
    shadow: styles.shadow,
    radius: typeof shapeRadius === 'number' ? shapeRadius : styles.radius ?? BUTTON_SQUIRCLE_RADIUS,
  };
}

/**
 * Button box className for squircle mode: layout + focus + the variant's non-surface classes + size
 * (with the `rounded-*` token removed, since the Squircle provides the shape) + the stacking context
 * (`relative isolate`) and `group` so the Squircle fill can react to the button's hover. Falls back to
 * the plain className for variants (or shapes) with no squircle surface.
 */
export function squircleButtonClassName(
  variant: ButtonVariant,
  size: ButtonSize,
  shape: ButtonShape = 'default',
): string {
  const styles = squircleButtonStyles[variant];
  if (!styles || shape === 'pill') return buttonClassName(variant, size, shape);
  const sizeNoRadius = sizeStyles[size].replace(RADIUS_TOKEN, '').replace(/\s{2,}/g, ' ').trim();
  return `${baseLayout} ${squircleBoxBase} ${styles.box} ${sizeNoRadius}`;
}

/** Everything a button-shaped element needs to render: its own classes, and its Squircle layer if any. */
export interface ButtonRendering {
  className: string;
  /** Props for the {@link Squircle} layer, or null when this button renders on plain `border-radius`. */
  squircle: {
    cornerRadius: number;
    fillClassName: string;
    borderClassName: string;
    shadowClassName?: string;
    outsetPx: number;
  } | null;
  /**
   * Props for the {@link BusyOutline} layer, resolved here for the same reason the Squircle's are:
   * the outline traces whichever silhouette this variant/shape actually renders, and reading that
   * back off `squircle`'s nullness at the call site would be the second place deciding it.
   *
   * `cornerSmoothing` is 0 on every plain path, because a plain button's corner is a `border-radius`
   * arc and a superellipse traced over it misses the corner it is meant to ride.
   */
  busyOutline: {
    cornerRadius: number | 'pill';
    cornerSmoothing?: number;
    outsetPx: number;
  };
  /**
   * Positioning the box needs ONLY while a layer is absolutely positioned inside it, and only on the
   * plain path: the squircle box already carries `relative isolate` for its own layer. An
   * absolutely-positioned outline in an unpositioned parent resolves against the page, so this is a
   * correctness class rather than styling — and which of the two paths lacks it is a property of the
   * table that picks between them, not of the component that consumes it.
   */
  busyPositionClassName: string;
}

/**
 * The single resolution `Button` and `LinkButton` share. Both render the same silhouette from the same
 * variant table, so the choice of path — and the Squircle props that follow from it — is decided here
 * rather than twice; a link styled as a button is then structurally the same control as the button.
 */
export function buttonRendering(
  variant: ButtonVariant = 'secondary',
  size: ButtonSize = 'md',
  shape: ButtonShape = 'default',
): ButtonRendering {
  const surface = squircleButtonSurface(variant, shape);
  if (!surface) {
    return {
      className: buttonClassName(variant, size, shape),
      squircle: null,
      busyOutline: {
        cornerRadius: plainButtonRadius(variant, shape),
        cornerSmoothing: 0,
        outsetPx: 1,
      },
      busyPositionClassName: 'relative',
    };
  }
  return {
    className: squircleButtonClassName(variant, size, shape),
    squircle: {
      cornerRadius: surface.radius,
      fillClassName: surface.fill,
      borderClassName: surface.border,
      shadowClassName: surface.shadow,
      // Covers the transparent 1px border the box keeps for outer-size parity with the plain path.
      outsetPx: 1,
    },
    // Same radius and the same outset as the fill it sits over, so the two silhouettes coincide.
    busyOutline: { cornerRadius: surface.radius, outsetPx: 1 },
    busyPositionClassName: '',
  };
}

/**
 * IconButton equivalent of {@link squircleButtonClassName} — shares {@link squircleBoxBase} so the
 * layout/parity tokens live in one place. `sizeClasses` is the IconButton's own size padding (it has
 * a different scale than text buttons). The ink and disabled treatment are the variant's own `box`,
 * as on a text button: a fixed `text-fg` drew `main`'s glyph in its own fill colour, so it vanished.
 */
export function squircleIconButtonClassName(variant: SquircleIconButtonVariant, sizeClasses: string): string {
  return `inline-flex items-center justify-center transition-all ${squircleBoxBase} ${SQUIRCLE_ICON_BUTTON_BOX[variant]} ${sizeClasses}`;
}

