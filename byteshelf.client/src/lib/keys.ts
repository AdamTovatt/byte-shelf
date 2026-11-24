/** Whether a key pressed here belongs to a field, as ⌘A selecting its text does. */
export function isTypingTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
}
