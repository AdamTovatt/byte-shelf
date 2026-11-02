import { useContext, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { MobileTopBarContext } from '../../contexts/MobileTopBarContext';

/**
 * Renders `children` at the right end of the phone's top bar (the one with the menu button), which
 * shows below the `md` width only. Nothing renders where the shell has no such bar.
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
