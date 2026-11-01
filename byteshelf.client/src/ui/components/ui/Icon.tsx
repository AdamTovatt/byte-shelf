import type { ComponentType, SVGProps } from 'react';

const DEFAULT_STROKE_WIDTH = 1.5;

/** React component type for SVG icons loaded via vite-plugin-svgr (?react). */
export type IconComponent = ComponentType<SVGProps<SVGSVGElement>>;

export interface IconProps {
  icon: IconComponent;
  size?: number;
  strokeWidth?: number;
  stroke?: string;
  fill?: string;
  className?: string;
  /**
   * Hide the glyph from the accessibility tree.
   *
   * Here rather than on a wrapper because this prop list is CLOSED — nothing extra is forwarded — so
   * a caller whose mark repeats an adjacent label had to wrap the icon in an `aria-hidden` span, and
   * two such call sites grew, each carrying its own comment explaining the same workaround. A mark
   * beside words that already say what it means is the ordinary case, not an exotic one.
   *
   * Not defaulted to `true`. An icon that is a control's ONLY content carries the meaning, and
   * silently hiding those would leave a screen reader with an unnamed button — a change that breaks
   * quietly, in the direction no test looks.
   */
  'aria-hidden'?: boolean;
}

export function Icon({
  icon: SvgIcon,
  size = 24,
  strokeWidth = DEFAULT_STROKE_WIDTH,
  stroke,
  fill,
  className,
  'aria-hidden': ariaHidden,
}: IconProps) {
  return (
    <SvgIcon
      width={size}
      height={size}
      strokeWidth={strokeWidth}
      {...(stroke !== undefined && { stroke })}
      {...(fill !== undefined && { fill })}
      {...(ariaHidden !== undefined && { 'aria-hidden': ariaHidden })}
      className={className}
    />
  );
}
