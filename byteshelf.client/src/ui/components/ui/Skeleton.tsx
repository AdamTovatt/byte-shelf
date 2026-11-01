import type { HTMLAttributes } from 'react';
import { cn } from '../../lib/utils';

export interface SkeletonProps extends HTMLAttributes<HTMLDivElement> {
  variant?: 'rectangle' | 'circle' | 'text';
  /** Color palette. `neutral` is gray (default); `brand` is indigo, used while AI auto-fill is generating. */
  tone?: 'neutral' | 'brand';
  /**
   * What the skeleton is drawn ON, which decides how dark its tint has to be.
   *
   * `card` (the default) is every raised surface — a `Card`, a panel, a form — and is where almost
   * every skeleton in the app sits. `canvas` is the page background, which is one step closer to the
   * card tint than a card is, so a card-tinted bar drawn there is very nearly invisible.
   *
   * A property of the GROUND rather than of the placeholder, which is why it is a separate prop from
   * {@link SkeletonProps.tone} rather than another tone: the two answer different questions, and a
   * brand skeleton on the canvas would need both.
   *
   * `paper` is a sheet of paper — a page-preview card's page panel, which pins the light theme of
   * the app's family (`usePaperTheme`). It is drawn from the theme's own steps rather than the
   * shimmer variables, one step darker than the paper with the paper itself as the highlight, so it
   * follows that pin, which re-declares the steps — and, for the cool `light` pin, not the shimmer
   * variables, which have no block of their own for it. Under
   * `prefers-reduced-motion` it stands still as a flat panel.
   *
   * `brand` has no canvas or paper tint today, because nothing draws one there. It falls back to the
   * card tint rather than failing — the wrong shade of the right colour, which is what the neutral
   * case looked like before this existed.
   */
  surface?: 'card' | 'canvas' | 'paper';
  /**
   * Drawn without motion, flat at the tint's base: a place held for something that is NOT on its way
   * (#2726) — a failed template card's rows for the counts and the New report it will not get unless
   * its parse is retried. The sweep says "coming", and here nothing is, while a bar of the same size
   * still keeps the card the height of its neighbours. The same flat tone a reduced-motion reader
   * sees on the paper shimmer.
   */
  still?: boolean;
  width?: string | number;
  height?: string | number;
  delay?: number;
}

const variantStyles: Record<NonNullable<SkeletonProps['variant']>, string> = {
  rectangle: 'rounded-lg',
  circle: 'rounded-full',
  text: 'rounded h-4',
};

const toneStyles: Record<
  NonNullable<SkeletonProps['tone']>,
  Record<NonNullable<SkeletonProps['surface']>, string>
> = {
  neutral: { card: 'animate-shimmer', canvas: 'animate-shimmer-canvas', paper: 'animate-shimmer-paper' },
  brand: { card: 'animate-shimmer-brand', canvas: 'animate-shimmer-brand', paper: 'animate-shimmer-brand' },
};

export function Skeleton({
  variant = 'rectangle',
  tone = 'neutral',
  surface = 'card',
  still = false,
  width,
  height,
  delay,
  className = '',
  style,
  ...props
}: SkeletonProps) {
  return (
    <div
      // The stable hook for a test or a CSS rule that needs to find a placeholder, the way `Tab`
      // exposes `data-active`: the shimmer classes are styling and free to be retuned, so a test
      // written against one of them is asserting on a private value.
      data-skeleton="true"
      className={cn(toneStyles[tone][surface], variantStyles[variant], still && 'skeleton-still', className)}
      style={{
        width: typeof width === 'number' ? `${width}px` : width,
        height: typeof height === 'number' ? `${height}px` : height,
        ...(delay != null ? { animationDelay: `${delay}ms` } : {}),
        ...style,
      }}
      {...props}
    />
  );
}
