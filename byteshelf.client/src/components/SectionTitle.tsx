import type { ReactNode } from 'react';

export interface SectionTitleProps {
  children: string;
  /** Beside the title, such as the section's one button. */
  actions?: ReactNode;
  /** One short line under the title saying what the section is for. */
  description?: string;
}

/**
 * The heading of one section of a page, such as Keys in a shelf's settings. A step above the field
 * labels under it, so the section reads as their heading rather than as one more of them.
 */
export function SectionTitle({ children, actions, description }: SectionTitleProps) {
  return (
    <div className="space-y-1">
      <div className="flex min-h-9 items-center justify-between gap-3">
        <h2 className="text-heading-sm text-fg">{children}</h2>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
      {description && <p className="text-body-md text-muted">{description}</p>}
    </div>
  );
}
