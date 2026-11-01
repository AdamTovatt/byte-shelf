import type { ReactNode } from 'react';
import { Squircle } from './Squircle';
import { ROUNDED_PX } from './squircleConstants';

type IconContainerSize = 'sm' | 'md' | 'lg' | 'xl';
type IconContainerVariant = 'default' | 'brand' | 'surface' | 'plain';

export interface IconContainerProps {
  children: ReactNode;
  size?: IconContainerSize;
  variant?: IconContainerVariant;
  className?: string;
}

// Dimensions only — the radius is provided by the Squircle (or by the plain fallback below).
const sizeBox: Record<IconContainerSize, string> = {
  sm: 'w-10 h-10',
  md: 'w-16 h-16',
  lg: 'w-20 h-20',
  xl: 'w-24 h-24',
};

// `sm` is rounded-md, the rest rounded-lg (see plainRadiusClass) — driven off the shared scale.
const sizeRadius: Record<IconContainerSize, number> = {
  sm: ROUNDED_PX.md,
  md: ROUNDED_PX.lg,
  lg: ROUNDED_PX.lg,
  xl: ROUNDED_PX.lg,
};
const plainRadiusClass: Record<IconContainerSize, string> = {
  sm: 'rounded-md',
  md: 'rounded-lg',
  lg: 'rounded-lg',
  xl: 'rounded-lg',
};

// Squircle surface per solid variant; `plain` has no surface and keeps a plain box.
const squircleSurface: Partial<Record<IconContainerVariant, { fill: string; border?: string; text: string }>> = {
  default: { fill: 'fill-panel', border: 'stroke-border', text: 'text-muted' },
  brand: { fill: 'fill-brand-9', text: 'text-on-brand' },
  surface: { fill: 'fill-brand-surface', text: 'text-brand-tint' },
};

const plainVariantStyles: Record<IconContainerVariant, string> = {
  default: 'bg-panel border border-border text-muted',
  brand: 'bg-brand-9 text-on-brand',
  surface: 'bg-brand-surface text-brand-tint',
  plain: 'text-muted',
};

export function IconContainer({
  children,
  size = 'md',
  variant = 'default',
  className = '',
}: IconContainerProps) {
  const surface = squircleSurface[variant];

  if (surface) {
    return (
      <div className={`relative isolate flex items-center justify-center ${sizeBox[size]} ${surface.text} ${className}`}>
        <Squircle cornerRadius={sizeRadius[size]} fillClassName={surface.fill} borderClassName={surface.border} />
        {children}
      </div>
    );
  }

  return (
    <div className={`relative flex items-center justify-center ${sizeBox[size]} ${plainRadiusClass[size]} ${plainVariantStyles[variant]} ${className}`}>
      {children}
    </div>
  );
}
