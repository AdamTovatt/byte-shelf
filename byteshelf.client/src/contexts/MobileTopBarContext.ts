import { createContext } from 'react';

/** Where in the phone's top bar a page may put things: beside the title, or over the whole bar. */
export interface MobileTopBarSlots {
  /** The bar after its menu button, for a page's own field and buttons. */
  actions: HTMLElement | null;
  /** The bar itself, for something that covers it while open: a search field, a selection's actions. */
  bar: HTMLElement | null;
}

/** Given by the app shell, which draws the bar; read by `MobileTopBarActions` and `MobileTopBarOverlay`. */
export const MobileTopBarContext = createContext<MobileTopBarSlots>({ actions: null, bar: null });
