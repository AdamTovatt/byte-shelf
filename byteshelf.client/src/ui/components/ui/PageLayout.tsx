import type { ReactNode } from 'react';
import { cn } from '../../lib/utils';
import { PAGE_CONTENT_WIDTH } from './pageWidth';

/**
 * The padding a page sits in, by name:
 *
 * - `standard` (the default, `p-6 md:p-8`) — forms, records and the tabbed layouts.
 * - `spacious` (`p-8 md:p-12 lg:p-16`) — the top-level destinations: dashboard, search, the
 *   template / pipeline / checklist lists, the organization pages.
 * - `compact` (`p-4`) — a page that wants its content close to the frame.
 */
export type PagePadding = 'standard' | 'spacious' | 'compact';

const PADDING_CLASS: Record<PagePadding, string> = {
  standard: 'p-6 md:p-8',
  spacious: 'p-8 md:p-12 lg:p-16',
  compact: 'p-4',
};

export interface PageLayoutProps {
  children: ReactNode;
  /** A named padding, never a class string, so the few page paddings stay a closed set. */
  padding?: PagePadding;
  className?: string;
}

/**
 * A padded page: the one content width (`max-w-4xl`, the project overview's) centred in the padding.
 *
 * There is no width prop, deliberately (#2455). The app has two page shapes — full-bleed (the
 * document editor, the assistant panel) and padded — and every padded page is this width. A prop
 * would let a third width creep back in one page at a time, which is how there came to be five.
 */
export function PageLayout({
  children,
  padding = 'standard',
  className,
}: PageLayoutProps) {
  return (
    <div className={PADDING_CLASS[padding]}>
      <div className={cn(PAGE_CONTENT_WIDTH, 'mx-auto', className)}>
        {children}
      </div>
    </div>
  );
}
