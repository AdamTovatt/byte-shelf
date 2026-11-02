import { useContext, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { MobileTopBarContext } from '../../contexts/MobileTopBarContext';

/**
 * Renders `children` in the phone's top bar after its menu button, filling the rest of it. The bar
 * shows below the `md` width only; nothing renders where the shell has no such bar.
 */
export function MobileTopBarActions({ children }: { children: ReactNode }) {
  const { actions } = useContext(MobileTopBarContext);
  return actions ? createPortal(children, actions) : null;
}

/** Renders `children` over the whole phone top bar; give them `absolute inset-0` and a background. */
export function MobileTopBarOverlay({ children }: { children: ReactNode }) {
  const { bar } = useContext(MobileTopBarContext);
  return bar ? createPortal(children, bar) : null;
}
