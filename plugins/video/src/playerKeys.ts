/** What a key does to the player wherever the focus is in the preview: play or pause, or a jump of so many seconds. */
export type KeyAction = { kind: 'toggle' } | { kind: 'jump'; seconds: number };

/** How far J and L jump. */
export const JUMP_SECONDS = 10;

/** How far the arrow keys move the seek bar while it has the focus. */
export const STEP_SECONDS = 5;

/**
 * What `key` does, pressed with nothing held: Space and K play or pause, J and L jump back and forward. Space on a
 * button presses that button, and nothing is done of a key typed into a field. The arrow keys are the seek bar's
 * while it has the focus, and else step between files.
 */
export function keyAction(key: string, target: EventTarget | null): KeyAction | null {
  if (isTyping(target)) return null;
  switch (key.toLowerCase()) {
    case ' ':
      return isButton(target) ? null : { kind: 'toggle' };
    case 'k':
      return { kind: 'toggle' };
    case 'j':
      return { kind: 'jump', seconds: -JUMP_SECONDS };
    case 'l':
      return { kind: 'jump', seconds: JUMP_SECONDS };
    default:
      return null;
  }
}

function isTyping(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
}

function isButton(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('button, a[href], [role="button"]') !== null;
}
