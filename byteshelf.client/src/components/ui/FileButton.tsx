import { useRef, type ReactNode } from 'react';
import { Button, type ButtonProps } from './Button';

export interface FileButtonProps extends Omit<ButtonProps, 'onClick' | 'onChange'> {
  /** Called with the files picked; the picker then resets, so picking the same file again works. */
  onFiles: (files: File[]) => void;
  multiple?: boolean;
  children?: ReactNode;
}

/** A button that opens the browser's file picker. */
export function FileButton({ onFiles, multiple = true, children, ...buttonProps }: FileButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <>
      <Button type="button" {...buttonProps} onClick={() => inputRef.current?.click()}>
        {children}
      </Button>
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
    </>
  );
}
