import { useRef, type MouseEvent, type PointerEvent } from 'react';

/**
 * Handlers that call `onClick` for a click with the mouse alone: a tap, with a finger or a pen, is the preview's,
 * which shows or hides its bars.
 */
export function useMouseClick(onClick: () => void): { onPointerDown: (event: PointerEvent) => void; onClick: (event: MouseEvent) => void } {
  const pressedWith = useRef('mouse');
  return {
    onPointerDown: (event) => {
      pressedWith.current = event.pointerType;
    },
    onClick: () => {
      if (pressedWith.current === 'mouse') onClick();
    },
  };
}
