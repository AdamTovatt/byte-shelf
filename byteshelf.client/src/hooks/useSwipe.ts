import { useRef, type HTMLAttributes, type TouchEvent } from 'react';
import { DRAGS_ITSELF } from '../lib/dragsItself';
import { axisOf, swipeOf, type Axis, type Swipes } from '../lib/preview/swipe';

/** Where a swipe starts, and along what it is left to the swipe rather than to what it starts on. */
interface Start {
  x: number;
  y: number;
  free: Record<Axis, boolean>;
}

/**
 * Hears one finger swiping left, right or down over an element. Touch events rather than pointer events, as
 * a browser goes on telling of a touch it scrolls with. A swipe is left to what it starts on when that
 * moves with a drag that way itself: something zoomed in, a player's controls, a field, a row that scrolls
 * sideways or, down, a pane scrolled from its top.
 */
export function useSwipe(swipes: Swipes): Pick<HTMLAttributes<HTMLElement>, 'onTouchStart' | 'onTouchEnd' | 'onTouchCancel'> {
  const start = useRef<Start | null>(null);

  return {
    onTouchStart: (event: TouchEvent) => {
      const { target, currentTarget, touches } = event;
      const isFree = (axis: Axis) => !dragsItself(target, currentTarget, axis);
      // A second finger is a pinch.
      start.current = touches.length === 1 ? { x: touches[0].clientX, y: touches[0].clientY, free: { across: isFree('across'), down: isFree('down') } } : null;
    },
    onTouchEnd: (event: TouchEvent) => {
      const from = start.current;
      start.current = null;
      const touch = event.changedTouches[0];
      if (!from || event.touches.length > 0 || !touch) return;
      const way = swipeOf({ x: touch.clientX - from.x, y: touch.clientY - from.y });
      if (way && from.free[axisOf(way)]) swipes[way]?.();
    },
    onTouchCancel: () => {
      start.current = null;
    },
  };
}

/** Whether `target`, or what holds it up to `area`, moves with a drag of its own along `axis`. */
function dragsItself(target: EventTarget, area: Element, axis: Axis): boolean {
  for (let element = target instanceof Element ? target : null; element && element !== area; element = element.parentElement) {
    if (element.hasAttribute(DRAGS_ITSELF) || element instanceof HTMLMediaElement || element instanceof HTMLInputElement) return true;
    const { overflowX, overflowY } = getComputedStyle(element);
    const scrollsAcross = isScrolling(overflowX) && element.scrollWidth > element.clientWidth;
    // Only one scrolled from its top: at its top, a pull down would scroll it nowhere.
    const scrollsBackUp = isScrolling(overflowY) && element.scrollTop > 0;
    if (axis === 'across' ? scrollsAcross : scrollsBackUp) return true;
  }
  return false;
}

function isScrolling(overflow: string): boolean {
  return overflow === 'auto' || overflow === 'scroll';
}
