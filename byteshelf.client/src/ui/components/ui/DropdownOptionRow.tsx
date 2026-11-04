import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { cn } from '../../lib/utils';
import { menuRowClass } from './menuPanelStyles';

/** A `{ value, label }` choice shared by the dropdown-style pickers (`Select`, `Combobox`). */
export interface Option {
  value: string;
  label: string;
  /** Drawn after the label at the row's right edge — a status the row should carry beside its name. */
  trailing?: ReactNode;
}

export interface DropdownOptionRowProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  label: string;
  /** The row is the currently-committed selection. */
  selected?: boolean;
  /** The row is the keyboard-highlighted candidate (combobox arrow navigation). */
  active?: boolean;
  /**
   * Rendered before the label, at its own size — a file glyph on a file row, an action glyph on a
   * menu row. Typically an `Icon`.
   */
  leading?: ReactNode;
  /**
   * Rendered after the label, pushed to the right edge. For the keyboard hint a row wants to teach
   * (`@`, `⇥`), which is why it does not shrink and the label truncates around it.
   */
  trailing?: ReactNode;
}

/**
 * The single styled option row used by every dropdown-style picker, so the selected/hover/active
 * styling lives in exactly one place. It is the shared menu row (`menuPanelStyles.ts`): an inset pill
 * that the hover, the keyboard highlight and the selected tint all paint in, which is why a caller no
 * longer passes the shape. Pass-through props (`onClick`, `role`, `id`, `aria-*`,
 * `onMouseDown`, …) let each picker layer on its own listbox/combobox semantics.
 *
 * <b>A row that cannot act and has to say why is a `DropdownItem`'s</b> (`disabledReason`, #2377),
 * not this row's: no picker built on this one refuses a row with a sentence.
 */
export function DropdownOptionRow({
  label,
  selected = false,
  active = false,
  leading,
  trailing,
  className,
  ...rest
}: DropdownOptionRowProps) {
  return (
    <button
      type="button"
      className={cn(
        'w-full flex items-center gap-2.5 px-3.5 py-2 text-body-md text-left transition-colors',
        menuRowClass({ selected, highlighted: active, canAct: !rest.disabled }),
        'text-fg',
        // A menu row that cannot act says so rather than looking live and doing nothing — the
        // hover highlight is suppressed with the same `disabled:` variant that fades it, so the two
        // cannot come apart.
        'disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent',
        className,
      )}
      {...rest}
    >
      {leading && <span className="shrink-0 flex items-center">{leading}</span>}
      <span className="truncate min-w-0 flex-1">{label}</span>
      {trailing && <span className="shrink-0 flex items-center">{trailing}</span>}
    </button>
  );
}
