import type { ReactNode, ButtonHTMLAttributes } from 'react';
import { Icon, type IconComponent } from './Icon';
import { Spinner } from './Spinner';
import { Squircle } from './Squircle';
import { iconButtonCornerClass, isSquircleIconButtonVariant, squircleButtonSurface, squircleIconButtonClassName, MAIN_SURFACE_CLASS, SECONDARY_SURFACE_CLASS, type ButtonShape } from './buttonStyles';


/**
 * The glyph each button size draws at by default.
 *
 * A default rather than the only answer: an artboard whose ink fills more of its 24-unit box than a
 * neighbour's reads as the larger icon at the same number of pixels, so a row of mixed icons
 * sometimes needs one of them stepped down to LOOK the same size. `iconSize` is that override, and
 * `ListButton` already spells the same prop with the same meaning.
 */
const iconSizeMap = {
  xs: 14,
  sm: 16,
  md: 20,
  lg: 24,
} as const;

/** The icon button's visual variants. Exported so pass-through callers (e.g. a control that
 *  forwards a `variant` straight to an `IconButton` trigger) can reference it instead of copying
 *  the union — keeping the two in lockstep. */
export type IconButtonVariant = 'default' | 'main' | 'secondary' | 'danger' | 'ghost';

export interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: IconButtonVariant;
  /**
   * `xs` is a 22px control with a 14px glyph, for a control set into something small: a canvas's own small toolbar
   * (`PanZoomCanvas`'s zoom), a reference tile's ✕, the drafted template's logo box. Never a page's own.
   */
  size?: 'xs' | 'sm' | 'md' | 'lg';
  /**
   * Silhouette, as on `Button`: `rounded-lg` by default, a fully-round `pill`, or the tighter `chip`
   * corner for a control inside a dense row. A prop rather than a `rounded-*` class because a class
   * cannot win here: the plain path already writes `rounded-lg`, and Tailwind emits the radius scale
   * in size order, so the larger of two radius tokens wins whatever the JSX order — and on the
   * squircle path the corner is painted by a layer a class does not reach at all.
   */
  shape?: ButtonShape;
  icon?: IconComponent;
  /**
   * Draw the glyph at this many pixels instead of the one `size` implies, leaving the button's own
   * padding and box alone.
   *
   * Optional, and unset means exactly what this button drew before the prop existed. It is a number
   * rather than a class because `Icon` sizes itself with `width`/`height` attributes: a Tailwind
   * utility CAN override those, but only by out-specifying a presentation attribute, which is why
   * the one call site that tried it before reached for `!important` to be sure. Same knob, one
   * layer up.
   */
  iconSize?: number;
  iconStrokeWidth?: number;
  iconClassName?: string;
  loading?: boolean;
  children?: ReactNode;
}

export function IconButton({
  variant = 'default',
  size = 'md',
  shape = 'default',
  className = '',
  icon,
  iconSize,
  iconStrokeWidth,
  iconClassName,
  children,
  disabled,
  loading = false,
  ...props
}: IconButtonProps) {
  const baseStyles = `inline-flex items-center justify-center ${iconButtonCornerClass(shape)} transition-all`;

  const variantStyles = {
    default: 'bg-transparent text-muted hover:text-fg hover:bg-row-hover disabled:cursor-not-allowed disabled:opacity-40',
    // `Button`'s `main`: the view's one main action, as an icon (the composer's send).
    main: `${MAIN_SURFACE_CLASS} border text-on-btn-main disabled:cursor-not-allowed disabled:opacity-40`,
    // The field look `Button`'s `secondary` takes, from the one constant both read.
    secondary: `${SECONDARY_SURFACE_CLASS} border text-fg disabled:cursor-not-allowed disabled:opacity-40`,
    danger: 'bg-transparent text-muted hover:text-danger-11 hover:bg-danger-a-3 disabled:cursor-not-allowed disabled:opacity-40',
    ghost: 'bg-transparent text-muted hover:text-fg hover:bg-surface disabled:cursor-not-allowed disabled:opacity-40',
  };

  const sizeStyles = {
    xs: 'p-1 text-xs',
    sm: 'p-1 text-sm',
    md: 'p-2 text-base',
    lg: 'p-3 text-lg',
  };

  // Squircle corners for the filled variants only. Skip the `sm` and `xs` sizes: at ~24px square or less
  // with a 12px radius it's effectively a circle, where smoothing reads as nothing. A `pill` has no squircle
  // either (`squircleButtonSurface` returns none for it), so it takes the plain path's `rounded-full`.
  const squircleVariant = isSquircleIconButtonVariant(variant) && size !== 'sm' && size !== 'xs' ? variant : null;
  const surface = squircleVariant ? squircleButtonSurface(squircleVariant, shape) : null;
  const combinedClassName = surface && squircleVariant
    ? `${squircleIconButtonClassName(squircleVariant, sizeStyles[size])} ${className}`
    : `${baseStyles} ${variantStyles[variant]} ${sizeStyles[size]} ${className}`;

  return (
    <button
      className={combinedClassName}
      disabled={disabled || loading}
      {...props}
    >
      {surface && (
        <Squircle
          cornerRadius={surface.radius}
          fillClassName={surface.fill}
          borderClassName={surface.border}
          shadowClassName={surface.shadow}
          outsetPx={1}
        />
      )}
      {loading ? (
        // The button's own ink: the brand arc all but vanished on `main`'s near-black fill.
        <Spinner size="sm" tone="inherit" />
      ) : icon ? (
        <Icon icon={icon} size={iconSize ?? iconSizeMap[size]} strokeWidth={iconStrokeWidth} className={iconClassName} />
      ) : (
        children
      )}
    </button>
  );
}
