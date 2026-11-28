/** Whether a key pressed here belongs to a field, as ⌘A selecting its text does. */
export function isTypingTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
}

/** Whether letters and Space pressed here are typed into a field: on a slider, which takes only the arrow keys and their like, they are not. */
export function takesTyping(target: EventTarget | null): boolean {
  return isTypingTarget(target) && !(target instanceof HTMLInputElement && target.type === 'range');
}

/** Whether `event`'s key presses what has the focus, as Space and Enter do a button or a link. */
export function pressesTarget(event: KeyboardEvent): boolean {
  return (event.key === ' ' || event.key === 'Enter')
    && event.target instanceof Element
    && event.target.closest('button, a[href], [role="button"]') !== null;
}

/** Whether `event`'s key was pressed with nothing held: no Alt, Ctrl, Meta or Shift, as a shortcut of a page's own is. */
export function isPlainKey(event: Pick<KeyboardEvent, 'altKey' | 'ctrlKey' | 'metaKey' | 'shiftKey'>): boolean {
  return !(event.altKey || event.ctrlKey || event.metaKey || event.shiftKey);
}
