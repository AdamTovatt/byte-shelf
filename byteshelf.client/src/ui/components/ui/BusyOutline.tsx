import type { AnimationEvent } from 'react';
import { DEFAULT_CORNER_SMOOTHING } from './squircleConstants';
import { useSquirclePathLayer } from './useSquirclePathLayer';

export interface BusyOutlineProps {
  /**
   * Corner radius in px, or `'pill'` for a fully-round end — resolved against the measured height,
   * which is the one radius a caller cannot know in advance.
   */
  cornerRadius: number | 'pill';
  /**
   * Figma corner smoothing. Pass the same value the surface uses so the stroke rides the silhouette
   * it is tracing; `0` for a plain `border-radius` corner, which is what the transparent button
   * variants have.
   */
  cornerSmoothing?: number;
  /**
   * Tailwind `stroke-*` utility. Defaults to `stroke-current` — the host's own text colour.
   *
   * Inherited rather than named, because a colour that reads on every surface is not a colour, it is
   * a relationship. `stroke-fg` was the first answer and is wrong for the public page's variants:
   * they paint from a FIXED palette rather than the theme's, so `landingCtaOnInk` (a fixed-white
   * fill) would take a themed near-white stroke in dark mode, invisible on the very band it stands
   * on. `currentColor` is whatever the host already chose for its own text, which by construction
   * contrasts with the fill behind it — including for `text`, whose colour is itself inherited from
   * wherever it was placed.
   */
  strokeClassName?: string;
  /** Stroke width in px. */
  strokeWidth?: number;
  /**
   * Play the exit instead of the arrival: the dash shrinks while the lap keeps turning, so the tail
   * catches the head and swallows it at whatever point it had reached.
   *
   * The caller is responsible for keeping this mounted until the exit ends —
   * `useBusyOutlineLifecycle` does that, and is what `Button` uses.
   */
  exiting?: boolean;
  /**
   * Forwarded to the animated `<path>`, so a caller can learn when the exit finished — filter on
   * `animationName`, since the arrival ends here too.
   *
   * The real DOM event rather than the narrower shape `useBusyOutlineLifecycle` accepts: this is
   * what React actually passes, and a direct caller has no reason to be denied `elapsedTime` or
   * `currentTarget`. The hook's parameter stays narrow so it can be called in a test without
   * building a synthetic event.
   */
  onAnimationEnd?: (event: AnimationEvent<SVGPathElement>) => void;
  /**
   * Extend the layer this many px beyond the parent's padding box on every side, so the stroke sits
   * on the element's OUTER edge rather than inside a border kept for size parity. Buttons pass 1,
   * matching `Squircle`.
   */
  outsetPx?: number;
  className?: string;
}

/**
 * A short stroke travelling once around an element's edge, forever — the "something is happening
 * here" indicator for a control that must keep its own label and shape while it waits.
 *
 * **It travels along the path; it does not rotate.** A rotating conic gradient is the usual way to
 * get this look and it is wrong on a button: rotation is angular, so the highlight races across the
 * short edges and crawls along the long ones, and the effect reads as a wobble rather than a lap. An
 * SVG stroke with a gap in it, moved by `stroke-dashoffset`, travels at constant speed whatever the
 * box's proportions.
 *
 * **It leaves the way it arrived, backwards.** The dash shrinks while the lap keeps turning, so the
 * tail closes on a head that is standing still and swallows it — the stroke goes back into the hole
 * it came out of. Nothing needs to know WHERE it stops, which is what makes an exit possible for work
 * of unknown length: both halves are rates, and the head's velocity is the difference between two
 * equal ones. `styles.css` argues the arithmetic; `useBusyOutlineLifecycle` is what holds the layer
 * on screen long enough to play it.
 *
 * **It arrives by growing out of a point.** At first only the dash LENGTH animates, with the offset
 * held at 0 — which pins the tail to the path's start while the head runs ahead, so the stroke reads
 * as emerging from a hole rather than fading in at full length. Once it is whole the travel takes
 * over. Two CSS animations on two different properties, so they compose instead of fighting.
 *
 * The emerge's duration is derived from the lap's rather than chosen, because the head's speed comes
 * from the dash's growth rate in one phase and from the lap in the other: pick them independently and
 * the head visibly changes pace at the handover. `animate-busy-outline` in `styles.css` carries both
 * the timing and that derivation.
 *
 * **`pathLength={100}` is what makes the gap a percentage.** It renormalises the path's own length to
 * 100 user units, so the dash beside it is a fixed FRACTION of the perimeter on a tiny icon button and
 * on a wide CTA alike, and one lap is exactly 100 units of offset whatever the real geometry. Without
 * it the dash would have to be computed from `getTotalLength()` on every resize, and would drift the
 * moment the shape changed — and the keyframes could not be two numbers at all, since a fraction of
 * the way round would be a different length per button.
 *
 * The fraction itself is deliberately not restated here: it is written once on the `strokeDasharray`
 * below and once in the keyframes that animate it, and a third copy in prose is the one that goes
 * stale. (It already did — a comment claiming a fifth outlived the change to a tenth.)
 *
 * The path comes from the same generator `Squircle` uses, at whatever smoothing the caller's surface
 * has, so on a filled button the stroke traces the superellipse rather than the rounded rectangle the
 * CSS box would give — the two are visibly different at the corner, which is the only place this is
 * ever seen against an edge.
 *
 * Sized and positioned by `useSquirclePathLayer`, which `Squircle` also uses — so the outline and the
 * fill it traces cannot land a pixel apart. It needs a positioned parent, like any absolute layer.
 * Unlike `Squircle` it paints in FRONT of the surface, since an outline behind an opaque fill would be
 * invisible.
 */
export function BusyOutline({
  cornerRadius,
  cornerSmoothing = DEFAULT_CORNER_SMOOTHING,
  strokeClassName = 'stroke-current',
  strokeWidth = 1.5,
  exiting = false,
  onAnimationEnd,
  outsetPx = 0,
  className = '',
}: BusyOutlineProps) {
  // Destructured at the call site rather than held as one object: `react-hooks/refs` treats a
  // hook-returned object that carries a ref as a ref itself, so every `layer.path` read during render
  // is reported as accessing a ref value. Destructuring is also how every `useMeasuredSize` caller
  // already reads its result.
  const { ref, path, viewBox, style } = useSquirclePathLayer({ cornerRadius, cornerSmoothing, outsetPx });

  return (
    <svg
      ref={ref}
      aria-hidden
      // A stable hook for a test or a capture spec. The stroke itself is inside a `<path>` that
      // exists only once the layer has measured a size, so happy-dom — which lays nothing out —
      // renders this element and nothing within it. Keying on the path would make "is this control
      // busy?" unanswerable in a unit test, and answerable only as `false`, which is worse.
      data-busy-outline
      className={`pointer-events-none absolute overflow-visible ${className}`}
      style={style}
      viewBox={viewBox}
      preserveAspectRatio="none"
    >
      {path && (
        <path
          d={path}
          fill="none"
          pathLength={100}
          strokeDasharray="10 90"
          strokeLinecap="round"
          strokeWidth={strokeWidth}
          vectorEffect="non-scaling-stroke"
          // The two classes list `busy-outline-travel` identically and at the same position, so the
          // lap survives the swap mid-stride rather than restarting from the path's start. See the
          // note on `.animate-busy-outline-exit` in `styles.css`.
          className={`${exiting ? 'animate-busy-outline-exit' : 'animate-busy-outline'} ${strokeClassName}`}
          onAnimationEnd={onAnimationEnd}
        />
      )}
    </svg>
  );
}
