import { Children, type ReactNode } from 'react';
import { Card } from '../ui';

export interface CardListProps {
  /** The rows, one list item each. */
  children: ReactNode;
  /** Classes for the card itself, such as `overflow-hidden` to clip row hover colours to its corners. */
  className?: string;
}

/**
 * Rows on one card with a line between each two. The lines sit on an inner list rather than on the
 * card, because the card's first child is its squircle layer: dividers on the card would give the
 * first real row a top line running past the rounded corners.
 */
export function CardList({ children, className }: CardListProps) {
  return (
    <Card variant="elevated" padding="none" className={className}>
      <ul className="divide-y divide-border">
        {Children.map(children, (row) => <li>{row}</li>)}
      </ul>
    </Card>
  );
}
