import type { ReactNode } from 'react';
import { PageLayout } from '../../ui';

export interface TabbedPageShellProps {
  /** The page's own header, tabs included. Each caller builds its own; only the frame is shared. */
  header: ReactNode;
  children: ReactNode;
}

/**
 * The frame a tabbed page hangs in (from ordo's): a full-height column whose header stays put while
 * the tab's content scrolls below it at the one padded-page width.
 */
export function TabbedPageShell({ header, children }: TabbedPageShellProps) {
  return (
    <div className="h-full flex flex-col">
      {header}
      {/* The scroll boundary; the header is outside it, so it never scrolls away. */}
      <div className="flex-1 min-h-0 overflow-y-auto">
        <PageLayout>{children}</PageLayout>
      </div>
    </div>
  );
}
