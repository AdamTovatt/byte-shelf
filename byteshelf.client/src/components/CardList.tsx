import { Children, type ReactNode } from 'react';
import { Card } from '../ui';

export interface CardListProps {
  /** The rows, one list item each. */
  children: ReactNode;
  /** Classes for the card itself, such as `overflow-hidden` to clip row hover colours to its corners. */
  className?: string;
  /**
   * Hides the line on each side of a lit row: one hovered (where there is hover) or marked
   * `data-active`, as a selected row is. A lit row's colour fills it edge to edge, so a line beside it
   * would split it into two bands. For lists whose rows light up; elsewhere a hover would only make
   * lines vanish.
   */
  hideDividersBesideLitRows?: boolean;
}

/**
 * A row's own line (its top border) and the next row's, hidden while the row is lit. Important,
 * because `divide-y`'s selector outweighs a plain utility on the row.
 */
const litRowDividers =
  'has-[[data-active]]:!border-transparent [li:has([data-active])+&]:!border-transparent ' +
  'hover:!border-transparent [@media(hover:hover)]:[li:hover+&]:!border-transparent';

/**
 * Rows on one card with a line between each two. The lines sit on an inner list rather than on the
 * card, because the card's first child is its squircle layer: dividers on the card would give the
 * first real row a top line running past the rounded corners.
 */
export function CardList({ children, className, hideDividersBesideLitRows = false }: CardListProps) {
  return (
    <Card variant="elevated" padding="none" className={className}>
      <ul className="divide-y divide-border">
        {Children.map(children, (row) => <li className={hideDividersBesideLitRows ? litRowDividers : undefined}>{row}</li>)}
      </ul>
    </Card>
  );
}
