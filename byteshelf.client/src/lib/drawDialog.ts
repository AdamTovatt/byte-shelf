import type { ReactNode } from 'react';

/** Draws a dialog that code shows, such as a `Modal`, which is shown until it calls `close`. */
export type DrawDialog = (close: () => void) => ReactNode;
