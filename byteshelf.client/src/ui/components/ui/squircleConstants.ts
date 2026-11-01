/**
 * Single source for the squircle corner scale. Mirrors the `borderRadius` tokens in
 * `tailwind.config.js` (`sm`/`md`/`lg`/`xl`/`2xl`), so a surface squircled in px stays in step with the
 * `rounded-*` token it replaces. Components map their own size preset to one of these keys rather than
 * re-encoding raw px (see Card / IconContainer / buttonStyles / Popover).
 */
export const ROUNDED_PX = {
  sm: 4,
  md: 8,
  lg: 12,
  xl: 16,
  '2xl': 20,
} as const;

/** Figma corner smoothing: 0 = a plain rounded corner, 1 = maximum squircle. 0.6 is a natural, iOS-like amount. */
export const DEFAULT_CORNER_SMOOTHING = 0.6;
