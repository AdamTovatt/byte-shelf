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
}

export function Icon({
  icon: SvgIcon,
  size = 24,
  strokeWidth = DEFAULT_STROKE_WIDTH,
  stroke,
  fill,
  className,
}: IconProps) {
  return (
    <SvgIcon
      width={size}
      height={size}
      strokeWidth={strokeWidth}
      {...(stroke !== undefined && { stroke })}
      {...(fill !== undefined && { fill })}
      className={className}
    />
  );
}
