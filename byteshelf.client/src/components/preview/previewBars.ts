import type { CSSProperties } from 'react';

/** Keeps an element out from under the preview's bars, as a viewer that is `clearOfBars` is drawn. */
export const CLEAR_OF_BARS = 'mt-[var(--preview-bar-top,0px)] mb-[var(--preview-bar-bottom,0px)]';

/** How high the preview's bottom bar stands, as a CSS length, for what a viewer draws above it, as a note over a player. */
export const PREVIEW_BAR_BOTTOM = 'var(--preview-bar-bottom, 0px)';

/** What the preview's area is styled with for {@link CLEAR_OF_BARS}: the height of its bars at the top and the bottom. */
export function barHeights(top: number, bottom: number): CSSProperties {
  return { '--preview-bar-top': `${top}px`, '--preview-bar-bottom': `${bottom}px` } as CSSProperties;
}
