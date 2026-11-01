import type { ReactNode } from 'react';
import { iconMap } from '../../lib/iconMap';
import { Icon } from './Icon';
import { Modal } from './Modal';
import { Button } from './Button';
import { ActionRow } from './ActionRow';
import { IconContainer } from './IconContainer';

export interface ConfirmationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title?: string;
  message: ReactNode;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'default';
  isLoading?: boolean;
  /**
   * Marks the rendered dialog card for a test to address, forwarded to {@link Modal.testId} — where
   * the reason it exists is written out in full.
   *
   * <b>Forwarded rather than reached around.</b> `Modal` has carried this prop since before this
   * wrapper existed, and the wrapper swallowed it — so a caller needing a scoped shot of a
   * CONFIRMATION had nothing to scope to, and no `getByRole` reach to fall back on either.
   *
   * <b>It is also what makes the confirm and cancel buttons addressable.</b> Without it a caller
   * whose page carries a button with the same words as `confirmText` — the one that OPENS the dialog
   * usually does — cannot tell the two apart: an accessible name matches as a case-insensitive
   * substring, so a role query finds both and the test is about whichever the DOM yields first.
   */
  testId?: string;
}

export function ConfirmationModal({
  isOpen,
  onClose,
  onConfirm,
  title,
  message,
  confirmText = 'Confirm',
  cancelText = 'Cancel',
  variant = 'default',
  isLoading = false,
  testId,
}: ConfirmationModalProps) {
  const handleConfirm = () => {
    onConfirm();
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={title}
      className="max-w-md"
      testId={testId}
    >
      <div className="space-y-4">
        <div className="flex items-start gap-4">
          {variant === 'danger' && (
            <div className="flex-shrink-0">
              <IconContainer size="sm">
                <Icon icon={iconMap.alert} size={24} className="text-fg" />
              </IconContainer>
            </div>
          )}
          <div className="flex-1">
            <div className="text-fg">{message}</div>
          </div>
        </div>
        <ActionRow className="pt-2">
          <Button
            type="button"
            variant="secondary"
            onClick={onClose}
            disabled={isLoading}
          >
            {cancelText}
          </Button>
          <Button
            type="button"
            variant={variant === 'danger' ? 'danger' : 'main'}
            onClick={handleConfirm}
            loading={isLoading}
            disabled={isLoading}
          >
            {confirmText}
          </Button>
        </ActionRow>
      </div>
    </Modal>
  );
}
