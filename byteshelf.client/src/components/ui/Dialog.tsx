import { useEffect, useRef, type FormEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Button } from './Button';

export interface DialogProps {
  open: boolean;
  title: string;
  onClose: () => void;
  /** Called when the form is submitted, by its button or Enter. */
  onSubmit?: () => void;
  submitLabel?: string;
  submitVariant?: 'primary' | 'danger';
  submitting?: boolean;
  /** Shown under the fields, such as the server's reason for refusing. */
  error?: string;
  children?: ReactNode;
}

/** A modal form over the page: a title, its fields, and a submit and a cancel button. Escape closes it. */
export function Dialog({
  open,
  title,
  onClose,
  onSubmit,
  submitLabel = 'Save',
  submitVariant = 'primary',
  submitting = false,
  error,
  children,
}: DialogProps) {
  const panelRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    if (!open) return;
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', onKeyDown);
    panelRef.current?.querySelector<HTMLElement>('input, button')?.focus();
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open, onClose]);

  if (!open) return null;

  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    onSubmit?.();
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <form
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onSubmit={handleSubmit}
        className="w-full max-w-md rounded-xl border border-border-light dark:border-border-dark bg-elevation-2-light dark:bg-elevation-2-dark p-5 shadow-lg space-y-4"
      >
        <h2 className="text-heading-sm text-text-1-light dark:text-text-1-dark">{title}</h2>
        {children}
        {error && <p className="text-body-md text-danger-light dark:text-danger-dark">{error}</p>}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="ghost" onClick={onClose}>
            {onSubmit ? 'Cancel' : 'Close'}
          </Button>
          {onSubmit && (
            <Button type="submit" variant={submitVariant} loading={submitting}>
              {submitLabel}
            </Button>
          )}
        </div>
      </form>
    </div>,
    document.body,
  );
}
