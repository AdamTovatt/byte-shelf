import { Input } from './Input';
import { IconButton } from './IconButton';
import { Tooltip } from './Tooltip';
import { useCopyFeedback } from '../../hooks/useCopyFeedback';

export interface CopyFieldProps {
  /** The text shown in the read-only field and written to the clipboard. */
  value: string;
  label?: string;
  /** Accessible label / idle tooltip for the copy button. Defaults to "Copy". */
  copyButtonAriaLabel?: string;
  className?: string;
}

/**
 * Read-only field with a one-click copy button (idle → copied → failed, auto-resetting).
 * Use for any value the user needs to copy verbatim — an invite link, a token, an id.
 * Composes the standard `Input` (read-only, monospace) with the copy button in its end slot.
 */
export function CopyField({ value, label, copyButtonAriaLabel = 'Copy', className }: CopyFieldProps) {
  const { state, copy, icon, statusText } = useCopyFeedback();
  const tooltipContent = state === 'idle' ? copyButtonAriaLabel : statusText;

  return (
    <>
      <Input
        label={label}
        value={value}
        readOnly
        onFocus={(event) => event.currentTarget.select()}
        className={`font-mono text-body-sm select-all ${className ?? ''}`}
        endElement={
          <Tooltip content={tooltipContent}>
            <IconButton
              type="button"
              size="md"
              variant="ghost"
              icon={icon}
              onClick={() => copy(value)}
              aria-label={copyButtonAriaLabel}
            />
          </Tooltip>
        }
      />
      {/* The icon + hover-only tooltip convey copied/failed visually; this announces it to screen
          readers. Empty while idle so the auto-reset doesn't read out anything. */}
      <span role="status" aria-live="polite" className="sr-only">
        {statusText}
      </span>
    </>
  );
}
