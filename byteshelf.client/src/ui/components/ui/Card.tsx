import { type ReactNode, type HTMLAttributes, type KeyboardEvent, forwardRef } from 'react';
import { Squircle } from './Squircle';
import { cardPaddingStyles, cardSquircleRadius, DEFAULT_CARD_PADDING, DEFAULT_CARD_SIZE, type CardPadding, type CardSize } from './cardStyles';

type CardTint = 'info' | 'success' | 'warning' | 'danger';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
  variant?: 'flat' | 'transparent' | 'elevated' | 'interactive';
  padding?: CardPadding;
  /** Size preset controlling border radius and padding scale. Default: 'lg'. */
  size?: CardSize;
  /** Semantic color tint — replaces the default background with a translucent status color. */
  tint?: CardTint;
  /** When false, the default background is omitted so the caller can supply their own. */
  defaultBackground?: boolean;
  /** When true, the background is translucent with backdrop blur. */
  translucent?: boolean;
  /**
   * Render Figma-style smooth (squircle) corners instead of a plain `border-radius`. On by default.
   *
   * Applies only to the solid `elevated` / `interactive` surfaces that use the card's own default
   * background, radius and clipping. It auto-falls-back to plain `border-radius` for `tint`,
   * `translucent`, `defaultBackground={false}`, or whenever the caller overrides the surface via
   * `className` (`bg-*`), the radius (`rounded-*`), or clipping (`overflow-*`) — because the
   * {@link Squircle} layer paints an opaque silhouette and cannot reproduce a custom/translucent
   * fill, nor clip overflowing children. `defaultBackground` is in that list because the silhouette
   * is painted `fill-panel`, which is precisely the background the flag says the caller is supplying
   * instead; it is easy to overlook because it normally travels with a `bg-*` that already triggered
   * the fallback by itself.
   * Pass `squircle={false}` to force the plain corner.
   */
  squircle?: boolean;
  /**
   * Give the card button semantics — `role="button"`, keyboard focus + Enter/Space activation,
   * a focus ring and a pointer cursor — on top of any `variant`'s surface. Use this when a
   * non-`interactive` surface (e.g. a `flat` card with bespoke state styling) still needs to be
   * clickable. The activation may be handled by the card's own `onClick`, or — when there is no
   * `onClick` — by an ancestor the synthesized click bubbles to (e.g. a Popover/Dropdown trigger),
   * which keeps that pattern keyboard-operable. The `interactive` variant already implies this when
   * given an `onClick`.
   */
  clickable?: boolean;
  /**
   * The shadow the squircle silhouette is painted with. Defaults to `drop-shadow-card-sm`, the
   * resting shadow of a card sitting IN a page.
   *
   * <b>A prop rather than a `className`, because the shadow is painted on the {@link Squircle} layer
   * and not on this box.</b> A `shadow-*` in `className` would draw a rectangle behind a superellipse,
   * and a `drop-shadow-*` there is a filter over the box's whole rendered result — it would shadow the
   * card's TEXT as well as its silhouette. Neither is the thing a caller wanting a deeper shadow
   * means; both look like they worked until you look closely.
   *
   * <b>Set it when the card FLOATS.</b> `drop-shadow-brand-lg` is what {@link ./Popover} paints for
   * every panel that lets it draw its own surface, so a card standing in for that surface — one
   * mounted in a `renderSurface={false}` popover — needs it here or it is the one floating panel in
   * the product wearing a resting card's shadow.
   *
   * Reaches only the squircle branch. A card that has fallen back to plain corners (see
   * {@link squircle}) keeps its variant's own `shadow-sm`, which is the pairing that branch has
   * always had.
   */
  shadowClassName?: string;
  children: ReactNode;
}

const solidBg = 'bg-panel';
const translucentBg = 'bg-panel/80 backdrop-blur-sm';

/**
 * Pointer + keyboard-focus affordance shared by every clickable card surface — the `interactive`
 * variant, its squircle box, and the `clickable` opt-in all compose from this one definition.
 */
const clickableAffordance =
  'cursor-pointer focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-tint focus-visible:ring-offset-2 focus-visible:ring-offset-bg';

const tintStyles: Record<CardTint, string> = {
  info: 'bg-info-a-3 backdrop-blur-sm',
  success: 'bg-success-a-3 backdrop-blur-sm',
  warning: 'bg-warning-a-3 backdrop-blur-sm',
  danger: 'bg-danger-a-3 backdrop-blur-sm',
};

const variantStyles: Record<NonNullable<CardProps['variant']>, string> = {
  flat: '',
  transparent: '',
  elevated: 'border border-border shadow-sm',
  interactive: `border border-border shadow-sm hover:border-surface-active transition-colors duration-200 ease-out ${clickableAffordance}`,
};

/**
 * The variants that paint a solid surface of their own, and so the only ones drawn on a squircle
 * silhouette. Nothing nested in the card changes look because of it: buttons are the same on a card
 * as on the canvas (#2455).
 */
const surfaceVariants = new Set<NonNullable<CardProps['variant']>>(['elevated', 'interactive']);

const sizeStyles: Record<CardSize, string> = {
  sm: 'rounded-lg',
  md: 'rounded-lg',
  lg: 'rounded-xl',
};

// Squircle box per surface variant: the non-surface classes (cursor / transition / focus ring / hover
// group), with the bg / border / shadow / radius dropped (the Squircle paints those). Explicit rather
// than regex-stripping the variant string — robust against a future variant gaining a border/shadow
// class that isn't meant to be removed. Keep in step with variantStyles' elevated/interactive.
const squircleBoxStyles: Record<'elevated' | 'interactive', string> = {
  elevated: '',
  // `group/card` is what the squircle edge's hover stroke reads (see `squircleBorderClassName`).
  interactive: `transition-colors duration-200 ease-out ${clickableAffordance} group/card`,
};

export const Card = forwardRef<HTMLDivElement, CardProps>(function Card({
  variant = 'elevated',
  padding = DEFAULT_CARD_PADDING,
  size = DEFAULT_CARD_SIZE,
  tint,
  defaultBackground = true,
  translucent = false,
  squircle = true,
  clickable = false,
  shadowClassName = 'drop-shadow-card-sm',
  className = '',
  children,
  ...props
}, ref) {
  const bg = tint
    ? tintStyles[tint]
    : defaultBackground && variant !== 'transparent'
      ? (translucent ? translucentBg : solidBg)
      : '';

  const hasSurface = surfaceVariants.has(variant);
  const appliedVariant = tint ? variantStyles[variant].replace(/shadow-\S+/g, '') : variantStyles[variant];

  // Squircle corners apply only to the solid surfaces using the card's own defaults. A caller
  // override of background / radius / shadow / clipping (or a tint / translucent surface) opts out,
  // since the Squircle paints an opaque silhouette and a caller box-shadow would sit behind it square.
  const hasSurfaceOverride = /\b(?:bg-|rounded-|shadow-|overflow-)/.test(className);
  const squircleActive =
    squircle && hasSurface && !tint && defaultBackground && !translucent && !hasSurfaceOverride;

  // Interactive cards shift their hover border colour; drive the Squircle border fill off the card's
  // own hover group so it tracks the same way the plain card's `hover:border-surface-active` does.
  const squircleBorderClassName =
    variant === 'interactive' ? 'stroke-border group-hover/card:stroke-surface-active' : 'stroke-border';

  // Button semantics: an explicit `clickable` opt-in always qualifies (its click may be handled by
  // the card's own `onClick` or by an ancestor the synthesized click bubbles to — e.g. a Popover
  // trigger), and the `interactive` variant qualifies when it carries an `onClick`.
  const isInteractive = clickable || (variant === 'interactive' && typeof props.onClick === 'function');
  // The `interactive` variant already bakes in the affordance; other variants opting in via
  // `clickable` need it appended (the keyboard/role come from interactiveProps below).
  const clickableExtras = clickable && variant !== 'interactive' ? clickableAffordance : '';
  const userOnKeyDown = props.onKeyDown;
  const interactiveProps: HTMLAttributes<HTMLDivElement> = isInteractive
    ? {
        role: props.role ?? 'button',
        tabIndex: props.tabIndex ?? 0,
        onKeyDown: (event: KeyboardEvent<HTMLDivElement>) => {
          userOnKeyDown?.(event);
          if (event.defaultPrevented) return;
          // Only respond to keys originating on the Card itself — descendants like buttons or
          // inputs handle their own activation, and a bubbled keydown should not synthesize a
          // second click on the Card.
          if (event.target !== event.currentTarget) return;
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            event.currentTarget.click();
          }
        },
      }
    : {};

  return (
    <div
      ref={ref}
      className={squircleActive
        ? `relative isolate ${squircleBoxStyles[variant as 'elevated' | 'interactive']} ${clickableExtras} ${cardPaddingStyles[size][padding].padding} ${className}`
        : `relative ${sizeStyles[size]} ${bg} ${appliedVariant} ${clickableExtras} ${cardPaddingStyles[size][padding].padding} ${className}`}
      {...props}
      {...interactiveProps}
    >
      {squircleActive && (
        <Squircle
          cornerRadius={cardSquircleRadius(size)}
          fillClassName="fill-panel"
          borderClassName={squircleBorderClassName}
          shadowClassName={shadowClassName}
        />
      )}
      {children}
    </div>
  );
});
