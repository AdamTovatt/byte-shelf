import { useRef, type ReactNode } from 'react';

/**
 * The browser's file picker, for any control to open: a menu item as well as a button. Render
 * `input` somewhere that stays mounted (not inside the menu, which closes as the item is clicked),
 * and call `open` from a click, which the browser requires before it shows the picker, with what the
 * files picked are for: as it was then, whatever is drawn by the time they are. The picker
 * resets after each pick, so picking the same file again works.
 */
export function useFilePicker(): { open: (onFiles: (files: File[]) => void) => void; input: ReactNode } {
  const inputRef = useRef<HTMLInputElement>(null);
  const onFilesRef = useRef<(files: File[]) => void>(() => undefined);

  const input = (
    <input
      ref={inputRef}
      type="file"
      multiple
      hidden
      data-testid="file-input"
      onChange={(event) => {
        const files = Array.from(event.target.files ?? []);
        event.target.value = '';
        if (files.length > 0) onFilesRef.current(files);
      }}
    />
  );

  function open(onFiles: (files: File[]) => void): void {
    onFilesRef.current = onFiles;
    inputRef.current?.click();
  }

  return { open, input };
}
