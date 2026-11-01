import type { ReactNode } from 'react';

type EmptyStateVariant = 'simple' | 'compact' | 'full';

export interface EmptyStateProps {
  variant?: EmptyStateVariant;
  /**
   * **Rendered by the `full` and `compact` variants only. The default `simple` variant drops it
   * silently** — see the last return below, which draws `message` and nothing else.
   *
   * Stated here because it is a property of this component, and two callers had each written their
   * own sentence about it, in different words, both pointing at this file for the fact that was not
   * in it. A caller passing a `title` without a `variant` gets no error, no warning and no title;
   * the title is simply not in the document.
   */
  title?: string;
  message: string;
  /** Drawn under the message by the `full` and `compact` variants; `simple` drops it, as it drops `title`. */
  action?: ReactNode;
  /**
   * The tag {@link title} is drawn as, on the `full` and `compact` variants. `h3` by default, which is what an
   * empty list inside a page that has its own heading is.
   *
   * `h1` is for a caller whose empty state IS the whole page — the not-found page, which says only
   * that the address goes nowhere and draws no other heading. There the chrome above it draws no
   * header row (`ordo.client`'s `lib/pageOwnedHeaderRoutes.ts`), so left at `h3` the document would
   * have no `h1` at all, and the words a reader actually sees would not be its heading.
   *
   * The two levels rather than a full range because those are the two real cases: a section of a
   * page, or the page. Anything between would be a caller guessing at a depth this component cannot
   * know.
   */
  headingLevel?: 'h1' | 'h3';
  className?: string;
}

export function EmptyState({
  variant = 'simple',
  title,
  message,
  action,
  headingLevel: Title = 'h3',
  className = '',
}: EmptyStateProps) {
  if (variant === 'full') {
    return (
      <div className={`flex flex-col items-center justify-center py-24 px-4 col-span-full ${className}`}>
        {title && (
          <Title className="text-heading-md text-fg mb-4 tracking-tight">
            {title}
          </Title>
        )}
        <p className="text-body-md text-muted text-center max-w-lg mb-8 leading-relaxed">
          {message}
        </p>
        {action}
      </div>
    );
  }

  // `compact` is `full` in a narrow pane's own small type, near its top rather than at its middle: centred across, as
  // `simple` is, with the title and action `simple` has no room for. `full`'s heading-sized title would wrap in a
  // column a few hundred pixels wide, and its middle is far down a pane as tall as the window (#2909).
  if (variant === 'compact') {
    return (
      <div className={`flex flex-col items-center gap-1 px-4 py-8 text-center ${className}`}>
        {title && <Title className="text-body-sm font-medium text-fg">{title}</Title>}
        <p className="text-body-sm text-muted">{message}</p>
        {action && <div className="mt-2">{action}</div>}
      </div>
    );
  }

  return (
    <div
      className={`text-muted text-center py-8 ${className}`}
    >
      {message}
    </div>
  );
}
