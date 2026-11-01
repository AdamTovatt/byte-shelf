import { getSvgPath } from 'figma-squircle';
import { useMeasuredSize } from './useMeasuredSize';
import { DEFAULT_CORNER_SMOOTHING } from './squircleConstants';

/** A radius per corner, in px, for a silhouette whose corners differ — 0 leaves that corner square. */
export interface SquircleCornerRadii {
  topLeft: number;
  topRight: number;
  bottomRight: number;
  bottomLeft: number;
}

/**
 * Companion to {@link Squircle} for containers that must *clip* their content to the silhouette —
 * e.g. a Popover/menu whose hovered rows paint a full-width background that would otherwise spill
 * past the rounded corners (CSS `overflow-hidden` only clips to a rounded rectangle, not the
 * squircle). Returns a `ref` to put on the content element and a `clipPath` to apply via `style`.
 *
 * `cornerRadius` is one radius for all four corners, or {@link SquircleCornerRadii} for a surface
 * rounded on some sides only — a band whose bottom edge runs flush into whatever is below it.
 *
 * Measures via {@link useMeasuredSize} (a callback ref), so it works for portaled / conditionally
 * rendered panels that mount after the consuming component's first render. Pair it with a Squircle of
 * the same radius for the fill/border behind.
 */
export function useSquircleClipPath<T extends HTMLElement = HTMLDivElement>(
  cornerRadius: number | SquircleCornerRadii,
  cornerSmoothing = DEFAULT_CORNER_SMOOTHING,
): { ref: (node: T | null) => void; clipPath: string | undefined } {
  const { ref, width, height } = useMeasuredSize<T>();
  const radii =
    typeof cornerRadius === 'number'
      ? { cornerRadius }
      : {
          topLeftCornerRadius: cornerRadius.topLeft,
          topRightCornerRadius: cornerRadius.topRight,
          bottomRightCornerRadius: cornerRadius.bottomRight,
          bottomLeftCornerRadius: cornerRadius.bottomLeft,
        };
  const clipPath =
    width > 0 && height > 0
      ? `path("${getSvgPath({ width, height, cornerSmoothing, ...radii })}")`
      : undefined;
  return { ref, clipPath };
}
