import { useMemo } from 'react';
import { shelfPasswords, type ShelfPasswords } from '../services/shelfPasswords';
import { usePasswordPrompt } from './usePasswordPrompt';

/** The passwords of a shelf's encrypted folders and files, asked for in the one prompt of a page. */
export function useShelfPasswords(shelf: string): ShelfPasswords {
  const ask = usePasswordPrompt();
  return useMemo(() => shelfPasswords(shelf, ask), [shelf, ask]);
}
