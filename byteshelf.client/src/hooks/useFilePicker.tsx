import { useRef, type ReactNode } from 'react';

/**
 * The browser's file picker, for any control to open: a menu item as well as a button. Render
 * `input` somewhere that stays mounted (not inside the menu, which closes as the item is clicked),
 * and call `open` from a click, which the browser requires before it shows the picker. The picker
 * resets after each pick, so picking the same file again works.
 */
export function useFilePicker(onFiles: (files: File[]) => void, multiple = true): { open: () => void; input: ReactNode } {
  const inputRef = useRef<HTMLInputElement>(null);

  const input = (
    <input
      ref={inputRef}
      type="file"
      multiple={multiple}
      hidden
      data-testid="file-input"
      onChange={(event) => {
        const files = Array.from(event.target.files ?? []);
        event.target.value = '';
        if (files.length > 0) onFiles(files);
      }}
    />
  );

  return { open: () => inputRef.current?.click(), input };
}
