import type { CSSProperties } from 'react';
import { getSvgPath } from 'figma-squircle';
import { useMeasuredSize } from './useMeasuredSize';

export interface SquirclePathLayerOptions {
  /** Corner radius in px, or `'pill'` for a fully-round end resolved against the measured height. */
  cornerRadius: number | 'pill';
  /** Figma corner smoothing: 0 is a plain rounded corner. */
  cornerSmoothing: number;
  /**
   * Extend the layer this many px beyond the parent's padding box on every side, to cover a
   * (transparent) CSS border the parent keeps for size parity.
   */
  outsetPx: number;
}

export interface SquirclePathLayer {
  /** Attach to the `<svg>`. Measuring the layer itself is what keeps it in step with its parent. */
  ref: (node: SVGSVGElement | null) => void;
  /** The silhouette, or `''` before the first measurement — render nothing while it is empty. */
  path: string;
  /** `viewBox` for the measured size, or `undefined` before there is one. */
  viewBox: string | undefined;
  /** Absolute position and size, including the outset. */
  style: CSSProperties;
}

/**
 * The scaffolding shared by every SVG layer painted over a container's silhouette: measure the box,
 * build the superellipse for it, and size the `<svg>` to cover it.
 *
 * Extracted because `Squircle` and `BusyOutline` had all of it twice — the same `useMeasuredSize`, the
 * same `hasSize` guard, the same `getSvgPath` call and the same outset arithmetic — and the second
 * copy's own doc admitted it, saying it was "sized and positioned exactly as `Squircle` is". Two
 * copies of positioning arithmetic is two chances for a layer to sit a pixel off the surface it is
 * supposed to be tracing, and the offset would look like a rendering artifact rather than a bug.
 * `useSquircleClipPath` is the precedent: the same `useMeasuredSize` + `getSvgPath` pairing, carved
 * out for the callers that clip rather than paint.
 *
 * **Explicit dimensions rather than `inset: 0`**, because an `<svg>` is a replaced element and will
 * not stretch to it. The layer measures ITSELF rather than its parent, which is what lets it be a
 * plain child of any positioned box.
 *
 * A `'pill'` radius is resolved here rather than by a caller, since half the height is a fact only the
 * measurement holds. Every radius is clamped to half the smaller side: `getSvgPath` draws nothing at
 * all for one larger than the box can hold, so an unclamped value fails by rendering emptiness.
 */
export function useSquirclePathLayer({
  cornerRadius,
  cornerSmoothing,
  outsetPx,
}: SquirclePathLayerOptions): SquirclePathLayer {
  const { ref, width, height } = useMeasuredSize<SVGSVGElement>();
  const hasSize = width > 0 && height > 0;

  const limit = Math.min(width, height) / 2;
  const radius = cornerRadius === 'pill' ? limit : Math.min(cornerRadius, limit);

  return {
    ref,
    path: hasSize ? getSvgPath({ width, height, cornerRadius: radius, cornerSmoothing }) : '',
    viewBox: hasSize ? `0 0 ${width} ${height}` : undefined,
    style: {
      top: -outsetPx,
      left: -outsetPx,
      width: outsetPx ? `calc(100% + ${2 * outsetPx}px)` : '100%',
      height: outsetPx ? `calc(100% + ${2 * outsetPx}px)` : '100%',
    },
  };
}
