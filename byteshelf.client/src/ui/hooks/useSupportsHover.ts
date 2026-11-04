import { useMediaQuery } from './useMediaQuery';

/**
 * Whether the primary pointing device can hover. Gate hover-driven scrollbar reveals on this: on
 * touch, a first tap synthesizes a mouseenter, and iOS Safari swallows that tap's click when it
 * reveals hover content — the double-tap this guards against. Touch still reveals the scrollbar on
 * the real scroll event, so gating the mouse-based reveal costs nothing there.
 */
export function useSupportsHover(): boolean {
  return useMediaQuery('(hover: hover)');
}
