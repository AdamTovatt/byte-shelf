import type { ShelfAccessPermission, ShelfKeyPermission } from '../services/accessService';

/** What a user may do on a shelf, as the choices read. */
export const ACCESS_OPTIONS: { value: ShelfAccessPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'readWrite', label: 'Read and write' },
];

/** What a key may do on one of its shelves, as the choices read. */
export const KEY_OPTIONS: { value: ShelfKeyPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'addOnly', label: 'Add only' },
  { value: 'readWrite', label: 'Read and write' },
];

export function keyPermissionLabel(permission: ShelfKeyPermission): string {
  return KEY_OPTIONS.find((option) => option.value === permission)?.label ?? permission;
}

/** The choice that takes a shelf away, beside a permission in a list of every shelf. */
export const NO_ACCESS = 'none';

export type AccessChoice = ShelfAccessPermission | typeof NO_ACCESS;
export type KeyAccessChoice = ShelfKeyPermission | typeof NO_ACCESS;

export const ACCESS_CHOICES: { value: AccessChoice; label: string }[] = [{ value: NO_ACCESS, label: 'None' }, ...ACCESS_OPTIONS];
export const KEY_CHOICES: { value: KeyAccessChoice; label: string }[] = [{ value: NO_ACCESS, label: 'None' }, ...KEY_OPTIONS];
