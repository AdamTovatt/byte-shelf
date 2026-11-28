/** Whether a key pressed here belongs to a field, as ⌘A selecting its text does. */
export function isTypingTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
}

/** Whether letters and Space pressed here are typed into a field: on a slider, which takes only the arrow keys and their like, they are not. */
export function takesTyping(target: EventTarget | null): boolean {
  return isTypingTarget(target) && !(target instanceof HTMLInputElement && target.type === 'range');
}
