/** What a key the preview hands the player does to it: play or pause, or a jump of so many seconds. */
export type KeyAction = { kind: 'toggle' } | { kind: 'jump'; seconds: number };

/** How far J and L jump. */
export const JUMP_SECONDS = 10;

/**
 * What `key` does, pressed with nothing held: Space and K play or pause, and J and L jump back and forward. The
 * preview hands the player no key typed into a field, nor Space on a button, which presses the button
 * (`usePreviewKeys`).
 */
export function keyAction(key: string): KeyAction | null {
  switch (key.toLowerCase()) {
    case ' ':
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
