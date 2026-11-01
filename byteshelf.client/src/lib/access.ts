import type { ShelfAccessPermission, ShelfKeyPermission } from '../services/accessService';

/** What a user may do on a shelf, as the choices read. */
export const ACCESS_OPTIONS: { value: ShelfAccessPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'readWrite', label: 'Read and write' },
];

/** What a key may do on its shelf, as the choices read. */
export const KEY_OPTIONS: { value: ShelfKeyPermission; label: string }[] = [
  { value: 'read', label: 'Read' },
  { value: 'addOnly', label: 'Add only' },
  { value: 'readWrite', label: 'Read and write' },
];

export function keyPermissionLabel(permission: ShelfKeyPermission): string {
  return KEY_OPTIONS.find((option) => option.value === permission)?.label ?? permission;
}
