import { useId } from 'react';
import { DEFAULT_CORNER_SMOOTHING } from './squircleConstants';
import { useSquirclePathLayer } from './useSquirclePathLayer';

export interface SquircleProps {
  /** Corner radius in px. Drive this from the same radius token the surface would otherwise use. */
  cornerRadius: number;
  /** Figma corner smoothing: 0 = a plain rounded corner, 1 = maximum squircle. Default 0.6 (a natural, iOS-like amount). */
  cornerSmoothing?: number;
  /** Tailwind `fill-*` utility for the surface colour, e.g. `fill-panel`. Keeps the silhouette theme-aware. */
  fillClassName?: string;
  /** Optional Tailwind `stroke-*` utility for a hairline border, e.g. `stroke-border`. Drawn as a clipped stroke on top of the fill, so it composites correctly even over a translucent fill. */
  borderClassName?: string;
  /** Border width in px when {@link borderClassName} is set. Default 1. */
  borderWidth?: number;
  /** Optional Tailwind `drop-shadow-*` utility for elevation. Unlike `box-shadow`, a drop-shadow follows the squircle silhouette. */
  shadowClassName?: string;
  /**
   * Extend the layer this many px beyond the parent's padding box on every side, to cover a
   * (transparent) CSS border the parent keeps for size parity. Default 0 (fills the padding box).
   * Buttons pass 1 so the silhouette reaches the element's outer edge over their 1px border.
   */
  outsetPx?: number;
  className?: string;
}

/**
 * Squircle background layer — paints a Figma-style smooth-corner (superellipse) silhouette
 * behind its container's content via an inline SVG path.
 *
 * Why an SVG fill and not `clip-path`: clipping would crop children, overflow and focus rings,
 * and clip away `box-shadow`. Painting the silhouette as a sibling layer leaves the real box
 * rectangular, so layout/overflow/children are untouched. Elevation rides on the SVG's
 * `drop-shadow` filter, which follows the curve in every browser — `box-shadow` cannot, because
 * it is geometrically locked to the (rounded-)rectangular border box.
 *
 * The fill is painted across the full silhouette as a single layer (so a translucent fill keeps its
 * intended opacity — important for variants like the danger button whose fill is an alpha tint). A
 * border (when {@link borderClassName} is set) is a `stroke` of twice the border width, clipped to the
 * silhouette so only the inner half shows: a hairline flush to the edge that composites over the fill
 * exactly as a real border composites over a background.
 *
 * Sizing, measurement and the path itself come from {@link useSquirclePathLayer}, shared with
 * `BusyOutline` so a fill and an outline over it cannot land a pixel apart. This one sits BEHIND
 * content via a negative z-index, so the parent must establish a stacking context (`relative isolate`).
 *
 * The corner path depends on the element's pixel size, so the layer measures itself with a
 * `ResizeObserver` and recomputes on resize. Cheap at a handful of surfaces; do not blanket it
 * across hundreds of elements.
 */
export function Squircle({
  cornerRadius,
  cornerSmoothing = DEFAULT_CORNER_SMOOTHING,
  fillClassName = 'fill-panel',
  borderClassName,
  borderWidth = 1,
  shadowClassName,
  outsetPx = 0,
  className = '',
}: SquircleProps) {
  const clipId = useId();
  // Destructured rather than held as one object — see the note in `BusyOutline`: `react-hooks/refs`
  // reads a property access on a ref-carrying object as accessing a ref during render.
  const { ref, path, viewBox, style } = useSquirclePathLayer({ cornerRadius, cornerSmoothing, outsetPx });

  return (
    <svg
      ref={ref}
      aria-hidden
      className={`pointer-events-none absolute -z-10 overflow-visible ${shadowClassName ?? ''} ${className}`}
      style={style}
      viewBox={viewBox}
      preserveAspectRatio="none"
    >
      {path && (
        <>
          {borderClassName && (
            <clipPath id={clipId}>
              <path d={path} />
            </clipPath>
          )}
          <path d={path} className={fillClassName} />
          {borderClassName && (
            <path
              d={path}
              fill="none"
              strokeWidth={borderWidth * 2}
              clipPath={`url(#${clipId})`}
              vectorEffect="non-scaling-stroke"
              className={borderClassName}
            />
          )}
        </>
      )}
    </svg>
  );
}
