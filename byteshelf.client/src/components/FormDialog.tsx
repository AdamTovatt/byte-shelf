import { useEffect, useRef, type FormEvent, type ReactNode } from 'react';
import { ActionRow, Button, FormError, Modal } from '../ui';

export interface FormDialogProps {
  open: boolean;
  title: string;
  onClose: () => void;
  /** Called when the form is submitted, by its button or Enter. Without it the dialog only has Close. */
  onSubmit?: () => void;
  submitLabel?: string;
  /** `danger` for what cannot be undone; the dialog's one main action otherwise. */
  submitVariant?: 'main' | 'danger';
  submitting?: boolean;
  /** The action shown but not yet possible, such as Move while the folder it is in is picked. */
  submitDisabled?: boolean;
  /** Shown under the fields, such as the server's reason for refusing. */
  error?: string;
  children?: ReactNode;
}

/** A modal form: a title, its fields, an error, and Cancel beside the one action. Escape closes it. */
export function FormDialog({
  open,
  title,
  onClose,
  onSubmit,
  submitLabel = 'Save',
  submitVariant = 'main',
  submitting = false,
  submitDisabled = false,
  error,
  children,
}: FormDialogProps) {
  const formRef = useRef<HTMLFormElement>(null);

  useEffect(() => {
    if (open) formRef.current?.querySelector<HTMLElement>('input:not([readonly]), button[type="submit"]')?.focus();
  }, [open]);

  function handleSubmit(event: FormEvent<HTMLFormElement>): void {
    event.preventDefault();
    if (!submitDisabled) onSubmit?.();
  }

  return (
    <Modal isOpen={open} onClose={onClose} title={title} className="max-w-md">
      <form ref={formRef} onSubmit={handleSubmit} className="space-y-4">
        {children}
        <FormError message={error} />
        <ActionRow className="pt-1">
          <Button type="button" variant="secondary" onClick={onClose}>
            {onSubmit ? 'Cancel' : 'Close'}
          </Button>
          {onSubmit && (
            <Button type="submit" variant={submitVariant} loading={submitting} disabled={submitDisabled}>
              {submitLabel}
            </Button>
          )}
        </ActionRow>
      </form>
    </Modal>
  );
}
