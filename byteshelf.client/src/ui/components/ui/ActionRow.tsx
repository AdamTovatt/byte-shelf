import type { ReactNode } from 'react';

export interface ActionRowProps {
  /** The buttons, the committing one last — it lands at the right edge. */
  children: ReactNode;
  /**
   * Below `sm`, stack the buttons full width, the committing one on top, for a form whose buttons
   * stretch on a phone. At `sm` and up the row is the same right-aligned row as ever.
   */
  stackBelowSm?: boolean;
  /**
   * The same stack, asked of the row's container rather than the window: full width, the committing button on top,
   * where the nearest ancestor declaring `[container-type:inline-size]` is narrower than 16rem, and the usual row from
   * there up. For a card in a resizable panel (a new template's question card, #3080), whose width follows the panel
   * rather than the window. The caller declares the container.
   */
  stackInNarrowContainer?: boolean;
  /**
   * One line with a smaller gap, never wrapping: for a popover whose width is set to fit its buttons (the Excel
   * report's cell and column popovers, #2763), where a wrapped committing button would sit alone under the others.
   */
  dense?: boolean;
  /** Spacing around the row (`pt-4`, `mt-3`). Not a gap or a justify: the row owns those. */
  className?: string;
}

/**
 * The row a form's, card's, section's or banner's action buttons sit in: right-aligned, the
 * committing button last, wrapping onto a further right-aligned line when it runs out of width
 * (#2455). One definition, so "action buttons sit on the right" is a component rather than a
 * convention each call site spells again with its own gap.
 *
 * Not `ActionBar`: that is a floating, elevated bar with a label and a dismiss ✕, for acting on a
 * selection. This is plain layout inside whatever surface it ends — no card, no label, no chrome.
 */
export function ActionRow({ children, stackBelowSm = false, stackInNarrowContainer = false, dense = false, className = '' }: ActionRowProps) {
  // Both stacks written out in full: Tailwind finds classes by scanning source text, so a prefix assembled at runtime
  // is a class it never generates.
  const direction = dense ? 'flex-nowrap items-center'
    : stackBelowSm ? 'flex-col-reverse sm:flex-row sm:flex-wrap sm:items-center'
      : stackInNarrowContainer
        ? 'flex-col-reverse [@container_(min-width:16rem)]:flex-row [@container_(min-width:16rem)]:flex-wrap [@container_(min-width:16rem)]:items-center'
        : 'flex-wrap items-center';
  return (
    <div data-action-row="" className={`flex ${direction} justify-end ${dense ? 'gap-2' : 'gap-3'} ${className}`}>
      {children}
    </div>
  );
}
