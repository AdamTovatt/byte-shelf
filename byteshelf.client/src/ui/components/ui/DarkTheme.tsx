import type { ReactNode } from 'react';
import { cn } from '../../lib/utils';

/**
 * Draws what it holds in the dark theme, whatever the page's: for what stands on dark whichever theme is
 * chosen. `data-theme` picks the dark set of colours, and the `.dark` class Tailwind's `dark:` variants.
 */
export function DarkTheme({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn('dark', className)} data-theme="dark">{children}</div>;
}
