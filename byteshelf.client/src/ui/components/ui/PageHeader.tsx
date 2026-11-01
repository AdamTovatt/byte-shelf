import { Children, type ReactNode } from 'react';
import { cn } from '../../lib/utils';

export interface PageHeaderProps {
  /** Title text. */
  title: ReactNode;
  /**
   * Which heading element the title is. Defaults to `h1`.
   *
   * A page has one `h1`, and where the dashboard chrome (#2105) draws its header row that one is the
   * chrome's: a visually-hidden `h1` taken from the breadcrumb's leaf. A page below THAT must pass
   * `h2`, or the same page is announced twice.
   *
   * **Which is not every route.** The shell withholds its header row — and that `h1` with it — for
   * the areas whose pages draw this header instead: in `ordo.client` the project subtree, an
   * organization's tab pages, Help and Admin, listed in `src/lib/pageOwnedHeaderRoutes.ts`. Those
   * four pass NOTHING and keep the default, because with the chrome's row withheld this header's
   * `h1` is the document's only one, and an `h2` there would leave it with none. The one
   * `ordo.client` caller that does pass `h2` is the component showcase, which demonstrates this
   * header on a route that IS under the chrome.
   *
   * The default stays `h1` for the other consumer too: `ordo.controlplane.client` renders its own
   * shell with no chrome at all, where this header is always the document's title.
   */
  headingLevel?: 'h1' | 'h2';
  /**
   * Optional content rendered under the title. Wrapped in the standard muted
   * subtitle styling — pass only the inner content (and any layout classes).
   * Clamped to a single line in tabless mode (there is no room to wrap on the
   * identity row); allowed to flow in tabbed mode.
   */
  subtitle?: ReactNode;
  /**
   * When false, the icon + title row is hidden but the tab row remains —
   * used to avoid a placeholder flash while the title is still loading.
   * Defaults to true.
   */
  showTitle?: boolean;
  /**
   * Optional content placed before the icon on the identity row — e.g. a back
   * button on a detail page. Present in both tabbed and tabless modes; being
   * part of the identity row, it is hidden along with it when `showTitle` is
   * false.
   */
  leading?: ReactNode;
  /**
   * Optional content directly to the right of the title — a control acting on the title itself, such
   * as the project header's rename pen (#2583). Not `actions`, which sit at the row's far end, away
   * from the words they would be about.
   *
   * Rendered BESIDE the heading element rather than inside it, so the heading's accessible name stays
   * the title alone: a button inside an `h1` contributes its own name to the heading's, and every
   * `getByRole('heading', { name })` on the page would then have to know about the button.
   *
   * The title and this slot share a `group/title` row that sizes to the title, so a caller can reveal a
   * control on hover of the title with `group-hover/title:`. Hidden with the rest of the identity row
   * when `showTitle` is false.
   */
  trailing?: ReactNode;
  /**
   * Optional right-aligned actions on the identity row. In tabless mode this is
   * the trailing-actions slot (there is no tab row to host them); in tabbed mode
   * it sits on the title row, above the tabs. Being part of the identity row, it
   * too is hidden when `showTitle` is false.
   */
  actions?: ReactNode;
  /**
   * Optional tab row — typically a `TabGroup` (plus any tab-strip-level trailing
   * content). For actions on the identity row, use the `actions` prop instead.
   *
   * The presence of a tab row is what selects the layout. Pass one and the
   * header renders in its **tabbed** form: the title row sits above the tabs and
   * the tab row supplies the bottom rhythm. Omit it and the header renders
   * **tabless**: a single identity row with symmetric vertical padding and no
   * tab strip.
   *
   * By design a *falsy* child reads as tabless, so a conditionally-rendered tab
   * row — `{showTabs && <TabGroup … />}` — flips the header to tabless when the
   * condition is false. That is intentional; do not "fix" it by wrapping the
   * tabs in an always-present element. Guard with a boolean, not a numeric length
   * (`{items.length && …}`) — a leftover `0` is kept as a child and would select
   * tabbed.
   */
  children?: ReactNode;
  className?: string;
}

/**
 * Shared page-level header: a bordered, panel-coloured bar carrying the page's
 * title. Used by the project and admin layouts (tabbed) and the
 * control-plane pages (tabless), so they share placement, spacing, and
 * background. See {@link PageHeaderProps.children} for how the tabbed vs.
 * tabless layout is selected.
 *
 * **There is no leading icon and no slot for one.** The header names the page in
 * words; the glyph that used to sit in front of the title — in a filled box,
 * then bare — said nothing the title did not, on a row that already carries the
 * page's identity. `SubPageHeader` keeps its own `icon`, and the difference is
 * the point: that one marks a section WITHIN a page, where a concept mark is
 * doing work no adjacent title is already doing.
 */
export function PageHeader({
  title,
  headingLevel: Heading = 'h1',
  subtitle,
  showTitle = true,
  leading,
  trailing,
  actions,
  children,
  className,
}: PageHeaderProps) {
  // Children.toArray drops falsy entries, so an evaluated-away conditional tab row reads as
  // tabless even when it leaves multiple falsy children — a raw Boolean(children) would see
  // the leftover `[false, false]` array as truthy and wrongly pick the tabbed layout.
  const hasTabRow = Children.toArray(children).length > 0;
  return (
    <div
      // The whole header, so a spec can make a claim about it rather than about one of its rows.
      // `page-header-tabs` below addresses only the tab strip, and a NEGATIVE scoped there — "this
      // action is not in the header" — is satisfied by the `actions` slot on the identity row, which
      // is precisely the corner such an assertion is usually guarding against.
      data-testid="page-header"
      className={cn(
        'border-b border-border bg-panel shrink-0',
        hasTabRow ? 'px-6 pt-4 pb-0' : 'px-4 py-3 sm:px-6',
        className,
      )}
    >
      {showTitle && (
        <div className={cn('flex items-center gap-3', hasTabRow && 'mb-3')}>
          {leading}
          <div className="min-w-0 flex-1">
            {/* Rendered through the tag name so the LEVEL is a prop and the styling is not: both
                levels look the same here, because what changes is where the page sits under the
                chrome's heading and not how its title reads.

                The title row is the `group/title` a `trailing` control's hover reveal reads. `w-fit`
                sizes it to the title, so hovering the empty width beside a short name is not hovering
                the name; `max-w-full` with the heading's `min-w-0` lets a long title still truncate
                rather than push the control out of the header. A block-level flex box rather than
                `inline-flex`, which would sit on a line box and add the parent's strut below it. */}
            <div className="group/title flex w-fit max-w-full items-center gap-1">
              <Heading className="text-heading-md text-fg truncate min-w-0">{title}</Heading>
              {trailing && <div className="flex shrink-0 items-center">{trailing}</div>}
            </div>
            {subtitle && (
              <div data-testid="page-header-subtitle" className={cn('mt-0.5 text-body-sm text-muted', !hasTabRow && 'truncate')}>{subtitle}</div>
            )}
          </div>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}
      {hasTabRow && (
        // Marked so a capture can frame the tab row on its own (#1980). The section-highlight shots
        // are about this strip and nothing else: a full-viewport shot of the same states would
        // composite a pipeline run's timestamps and durations, and one scoped to the content region
        // would leave the tabs — the whole subject — out of frame.
        //
        // `e2e/helpers.ts` addresses it as `pageHeaderTabs`, and five specs reach it through that —
        // including four capture calls in `project-section-tab-highlight.spec.ts`, which frame this
        // strip and nothing else.
        <div data-testid="page-header-tabs" className="flex items-center">
          {children}
        </div>
      )}
    </div>
  );
}
