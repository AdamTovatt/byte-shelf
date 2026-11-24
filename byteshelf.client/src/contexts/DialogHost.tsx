import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { CleanupBoundary } from '../components/CleanupBoundary';
import { OverlaySlotContext, type OverlaySlot } from '../ui/hooks/useOverlay';
import { useClosedWhenGone } from '../hooks/useClosedWhenGone';
import type { DrawDialog } from '../lib/drawDialog';

/** A dialog shown by code, until it is closed. */
export interface ShownDialog {
  close: () => void;
  /** Resolves once it is closed, by itself or by what showed it. */
  closed: Promise<void>;
}

/** Shows what `draw` draws until it is closed. */
export type ShowDialog = (draw: DrawDialog) => ShownDialog;

interface HeldDialog {
  id: number;
  draw: DrawDialog;
  close: () => void;
}

const ShowContext = createContext<ShowDialog | null>(null);

/**
 * Holds the dialogs that code shows, rather than a page's own state, such as a password prompt or a file
 * action's: apart from the pages, so that one stays as it is while what is under it changes. The last to
 * open is on top, whatever it opened over, and keeps its place while what it draws changes (`useOverlay`).
 */
export function DialogHost({ children }: { children: ReactNode }) {
  const [held, setHeld] = useState<readonly HeldDialog[]>([]);
  const count = useRef(0);

  const show = useCallback<ShowDialog>((draw) => {
    const id = ++count.current;
    let close!: () => void;
    const closed = new Promise<void>((resolve) => {
      close = () => {
        setHeld((all) => all.filter((each) => each.id !== id));
        resolve();
      };
    });
    setHeld((all) => [...all, { id, draw, close }]);
    return { close, closed };
  }, []);

  return (
    <ShowContext.Provider value={show}>
      {children}
      <CleanupBoundary>
        {held.map(({ id, draw, close }) => <InSlot key={id}>{draw(close)}</InSlot>)}
      </CleanupBoundary>
    </ShowContext.Provider>
  );
}

/** One dialog, in one place however it is drawn. */
function InSlot({ children }: { children: ReactNode }) {
  const [slot] = useState<OverlaySlot>(() => ({ openCount: 0, order: null }));
  return <OverlaySlotContext.Provider value={slot}>{children}</OverlaySlotContext.Provider>;
}

/** Shows dialogs until the component using it is gone, which closes them; none is shown after that. */
export function useShowDialog(): ShowDialog {
  const show = useContext(ShowContext);
  if (!show) throw new Error('useShowDialog must be used within a DialogHost');

  const heldRef = useClosedWhenGone<ShownDialog>();
  return useCallback<ShowDialog>((draw) => {
    const { open, isGone } = heldRef.current;
    if (isGone) return { close: () => undefined, closed: Promise.resolve() };
    const dialog = show(draw);
    open.add(dialog);
    void dialog.closed.then(() => open.delete(dialog));
    return dialog;
  }, [show, heldRef]);
}
