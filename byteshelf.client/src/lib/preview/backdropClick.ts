import { createContext } from 'react';

/**
 * What a click with the mouse beside what is shown does, as one on a dialog's backdrop does: the preview's
 * closes it. Null where such a click does nothing.
 */
export const BackdropClick = createContext<(() => void) | null>(null);
