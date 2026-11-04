import { useCallback, useState } from 'react';

/**
 * State that is either controlled by the parent or self-managed. When `controlled` is defined the
 * value is owned by the parent and the returned setter only notifies `onChange`; otherwise the value
 * lives in internal state and the setter both updates it and notifies `onChange`. The setter is
 * stable across renders (as long as `onChange` is).
 *
 * Collapses the controlled/uncontrolled boilerplate shared by `Popover`, `QuickPrompt`,
 * `ConfirmDelete`, `ExpandableCard`, etc.
 */
export function useControllableState<T>(
  controlled: T | undefined,
  onChange: ((value: T) => void) | undefined,
  defaultValue: T,
): [T, (value: T) => void] {
  const isControlled = controlled !== undefined;
  const [uncontrolled, setUncontrolled] = useState(defaultValue);
  const value = isControlled ? controlled : uncontrolled;

  const setValue = useCallback((next: T) => {
    if (!isControlled) setUncontrolled(next);
    onChange?.(next);
  }, [isControlled, onChange]);

  return [value, setValue];
}
