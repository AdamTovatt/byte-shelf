import { useCallback, useEffect, useRef, useState, type FocusEvent, type HTMLAttributes, type PointerEvent } from 'react';

/** How long the controls stay with nothing happening. */
const IDLE_MS = 2000;

/** What does something when it is tapped. */
const ACTS = 'a[href], button, input, select, textarea, label, summary, video, audio, [role="button"], [role="menuitem"], [role="tab"]';

/** Why the controls stay however long nothing happens. */
type Hold = 'pointer' | 'focus' | 'frame';

/** Controls floated over what they control, that go once nothing has happened for a while. */
export interface FadingControls {
  isShown: boolean;
  /**
   * Spread on what the controls float over: a click on it or the mouse moving brings them back, and a tap
   * shows or hides them. Keys do not.
   */
  area: Pick<HTMLAttributes<HTMLElement>, 'onClick' | 'onPointerDown' | 'onPointerMove' | 'onPointerOver'>;
  /** Spread on each group of controls, which stay while the pointer or the focus is on one. */
  group: Pick<HTMLAttributes<HTMLElement>, 'onPointerEnter' | 'onPointerLeave' | 'onFocus' | 'onBlur'>;
}

/** Shows controls, and hides them after a while of no clicks and no moves of the mouse, or at a tap. */
export function useFadingControls(): FadingControls {
  const [isShown, setIsShown] = useState(true);
  const holds = useRef(new Set<Hold>());
  const timer = useRef<number | undefined>(undefined);
  /** Where the mouse last was, as a browser also tells of a move when what is under a still mouse changes. */
  const lastPoint = useRef<{ x: number; y: number } | null>(null);
  /** What the last click was made with: a click does not say so in every browser. */
  const pressedWith = useRef('mouse');

  // A timer rather than state per move, so that moving the mouse does not render what is shown under it.
  const countDown = useCallback(() => {
    window.clearTimeout(timer.current);
    if (holds.current.size === 0) timer.current = window.setTimeout(() => setIsShown(false), IDLE_MS);
  }, []);

  const wake = useCallback(() => {
    setIsShown(true);
    countDown();
  }, [countDown]);

  const hold = useCallback((why: Hold) => {
    holds.current.add(why);
    wake();
  }, [wake]);

  const release = useCallback((why: Hold) => {
    if (holds.current.delete(why)) wake();
  }, [wake]);

  useEffect(() => {
    countDown();
    return () => window.clearTimeout(timer.current);
  }, [countDown]);

  function onPointerMove(event: PointerEvent): void {
    if (event.pointerType !== 'mouse') return;
    const point = { x: event.clientX, y: event.clientY };
    if (lastPoint.current?.x === point.x && lastPoint.current.y === point.y) return;
    lastPoint.current = point;
    // Back from a frame, whose moves are its own document's.
    if (holds.current.has('frame')) release('frame');
    else wake();
  }

  return {
    isShown,
    area: {
      onClick: (event) => {
        // A tap on what does something, a control or a player's, is for that, and keeps the controls.
        const isTapAway = pressedWith.current === 'touch' && !(event.target instanceof Element && event.target.closest(ACTS));
        if (!isTapAway || !isShown) {
          wake();
          return;
        }
        window.clearTimeout(timer.current);
        setIsShown(false);
      },
      onPointerDown: (event) => {
        pressedWith.current = event.pointerType;
      },
      onPointerMove,
      // Nothing is heard of a mouse over an embedded page, a PDF's say, so the controls stay until it leaves it.
      onPointerOver: (event) => {
        if (event.pointerType === 'mouse' && event.target instanceof HTMLIFrameElement) hold('frame');
      },
    },
    group: {
      onPointerEnter: (event) => {
        if (event.pointerType === 'mouse') hold('pointer');
      },
      onPointerLeave: () => release('pointer'),
      // Focus from the keyboard: a button clicked keeps the focus too, and would keep its controls for good.
      onFocus: (event: FocusEvent) => {
        if (event.target instanceof HTMLElement && event.target.matches(':focus-visible')) hold('focus');
      },
      onBlur: (event: FocusEvent) => {
        if (!event.currentTarget.contains(event.relatedTarget)) release('focus');
      },
    },
  };
}
