import { useRef, type HTMLAttributes, type TouchEvent } from 'react';
import { DRAGS_ITSELF } from '../lib/dragsItself';

/** How far a finger goes across for a swipe, in pixels. */
const MIN_DISTANCE = 50;

/** How many times further across than up or down a swipe goes. */
const MIN_SLANT = 1.5;

interface Swipes {
  /** The finger went left: what comes next. */
  onLeft?: (() => void) | null;
  /** The finger went right: what came before. */
  onRight?: (() => void) | null;
}

/**
 * Hears one finger swiping left or right across an element. Touch events rather than pointer events, as
 * a browser goes on telling of a touch it scrolls with. A swipe is left to what it starts on when that
 * moves with a drag itself: something zoomed in, a player's controls or a row that scrolls sideways.
 */
export function useSwipe({ onLeft, onRight }: Swipes): Pick<HTMLAttributes<HTMLElement>, 'onTouchStart' | 'onTouchEnd' | 'onTouchCancel'> {
  const start = useRef<{ x: number; y: number } | null>(null);

  return {
    onTouchStart: (event: TouchEvent) => {
      // A second finger is a pinch.
      const isSwipe = event.touches.length === 1 && !dragsItself(event.target, event.currentTarget);
      start.current = isSwipe ? { x: event.touches[0].clientX, y: event.touches[0].clientY } : null;
    },
    onTouchEnd: (event: TouchEvent) => {
      const from = start.current;
      start.current = null;
      const touch = event.changedTouches[0];
      if (!from || event.touches.length > 0 || !touch) return;
      const across = touch.clientX - from.x;
      const upOrDown = touch.clientY - from.y;
      if (Math.abs(across) < MIN_DISTANCE || Math.abs(across) < Math.abs(upOrDown) * MIN_SLANT) return;
      (across < 0 ? onLeft : onRight)?.();
    },
    onTouchCancel: () => {
      start.current = null;
    },
  };
}

/** Whether `target`, or what holds it up to `area`, moves with a drag of its own. */
function dragsItself(target: EventTarget, area: Element): boolean {
  for (let element = target instanceof Element ? target : null; element && element !== area; element = element.parentElement) {
    if (element.hasAttribute(DRAGS_ITSELF) || element instanceof HTMLMediaElement || element instanceof HTMLInputElement) return true;
    const { overflowX } = getComputedStyle(element);
    if ((overflowX === 'auto' || overflowX === 'scroll') && element.scrollWidth > element.clientWidth) return true;
  }
  return false;
}
