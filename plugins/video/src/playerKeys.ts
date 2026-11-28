/** What a key the preview hands the player does to it: play or pause, or a jump of so many seconds. */
export type KeyAction = { kind: 'toggle' } | { kind: 'jump'; seconds: number };

/** How far J and L jump. */
export const JUMP_SECONDS = 10;

/** How far the arrow keys move the seek bar while it has the focus. */
export const STEP_SECONDS = 5;

/**
 * What `key` does, pressed with nothing held: Space and K play or pause, J and L jump back and forward. Space on a
 * button presses that button. A key typed into a field the preview hands no viewer. The arrow keys are the seek
 * bar's while it has the focus, and else step between files.
 */
export function keyAction(key: string, target: EventTarget | null): KeyAction | null {
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

function isButton(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest('button, a[href], [role="button"]') !== null;
}
