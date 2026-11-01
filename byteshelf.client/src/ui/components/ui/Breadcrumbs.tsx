import { Fragment } from 'react';
import { Link } from 'react-router-dom';
import { Icon, type IconComponent } from './Icon';
import { Skeleton } from './Skeleton';
import { cn } from '../../lib/utils';

/** One crumb: its words, where it leads, and whether the words are still being read. */
export interface BreadcrumbItem {
  /** Stable identity for the crumb — the React key, and what the app's own invariants compare. */
  key: string;
  label: string;
  /** Absolute address, or `null` for a crumb naming something with no page of its own. */
  to: string | null;
  /** True while the label stands in for a read that has not landed. Renders as a skeleton. */
  pending?: boolean;
  /**
   * An optional mark to the left of the words, naming what KIND of thing the crumb is.
   *
   * Optional rather than required because a crumb can name something the app has no icon for, and a
   * placeholder glyph would say less than nothing. Where one is given it should be the SAME icon the
   * app uses for that thing elsewhere — the sidebar's Help icon for the Help crumb, the project icon
   * for a project — so the trail and the navigation agree about what a thing looks like.
   *
   * Purely decorative: it is `aria-hidden`, and the crumb's own words are what is announced. An icon
   * that carried meaning the label did not would be a fact only sighted readers get.
   */
  icon?: IconComponent;
}

export interface BreadcrumbsProps {
  items: BreadcrumbItem[];
  /**
   * How large the trail is set.
   *
   * `lg` (the default) is the chrome's own naming of the page — one step above the body copy it sits
   * over. `md` is for a trail that shares its row with controls rather than heading a page: the
   * project header's, where the same 14px the app defaults to keeps the row one line high.
   */
  size?: 'md' | 'lg';
  className?: string;
}

/** The width a pending crumb reserves — a plausible name's worth, so the trail does not jump far. */
const PENDING_LABEL_WIDTH = '6rem';

/**
 * The trail across the top of the dashboard: where the reader is, and every step back out of it.
 *
 * Presentational, and deliberately so — it takes the crumbs it is given and derives nothing. Which
 * crumbs a route shows is a fact about the app's routes, so it lives in the app
 * (`ordo.client/src/lib/breadcrumbs.ts`), where the route table it is checked against also lives.
 *
 * **The last crumb never links.** It names the page the reader is already on, so it renders as text
 * carrying `aria-current="page"` whatever its `to` says — which is what lets the app give a crumb
 * one address and use it at both positions: a leaf here, a step in the trail on a deeper route. A
 * PENDING last crumb still carries it, on the placeholder standing in for the words.
 *
 * A crumb whose `to` is `null` renders as text at ANY position, not only the last (D1/#2105) — a
 * section of the trail that is a tab held in state rather than a route is the case.
 *
 * Not to be confused with `BreadcrumbNav` and `DetailBreadcrumb`, which predate the dashboard chrome:
 * this is the one that renders real `<Link>`s and takes a whole trail; those take one or two
 * hand-built steps and navigate imperatively.
 *
 * Those two have **no consumers at all** — not in either client, not in the showcase — and had none
 * before this component existed either, so they are pre-existing dead code rather than the thing the
 * unmigrated pages use. Stated because the first draft of this note guessed the opposite, and a
 * comment asserting a component is in use is what stops the next person checking.
 */
/**
 * What a crumb shows while its name is still being read: the label to a screen reader, a placeholder
 * to the eye. Shared so the linked and unlinked positions cannot drift apart in what they announce.
 */
function PendingLabel({ label }: { label: string }) {
  return (
    <>
      <span className="sr-only">{label}</span>
      <Skeleton
        aria-hidden="true"
        variant="text"
        surface="canvas"
        width={PENDING_LABEL_WIDTH}
        height="1rem"
      />
    </>
  );
}

/**
 * The icon size the trail draws at — matched to the 16px `text-body-lg` of the `lg` size.
 *
 * The same at `md`, deliberately: a mark two pixels smaller does not read as a smaller mark, it reads
 * as a blurrier one, and the row it sits in is already sized by its own controls.
 */
const CRUMB_ICON_SIZE = 16;

/**
 * A crumb's contents: its optional mark, then its words.
 *
 * One component for all four positions — pending or resolved, linked or not — because the icon has
 * to appear identically in each and four copies of that is four places to forget one.
 *
 * `aria-hidden` on the glyph: the mark repeats what the label already says, and a screen reader
 * announcing it as well would read the kind of thing twice.
 *
 * `truncate` sits on the WORDS rather than on the row. On the row it would clip the icon first, since
 * the icon is the leading child — a crumb whose name is too long for the space would lose its mark
 * and keep the overflow, which is backwards.
 */
function CrumbContent({ item }: { item: BreadcrumbItem }) {
  return (
    <>
      {item.icon && (
        <Icon icon={item.icon} size={CRUMB_ICON_SIZE} aria-hidden className="shrink-0" />
      )}
      {item.pending ? <PendingLabel label={item.label} /> : <span className="truncate">{item.label}</span>}
    </>
  );
}

export function Breadcrumbs({ items, size = 'lg', className }: BreadcrumbsProps) {
  if (items.length === 0) return null;

  return (
    <nav aria-label="Breadcrumb" className={cn('min-w-0', className)}>
      {/* At `lg`, `text-body-lg` (16px) rather than the 14px the app defaults to: the trail is the
          chrome's own naming of the page rather than body copy inside it, so it is set one step above
          the content it sits over — the size the leaf reads at is the size the page's name reads at.
          At `md` it comes back down to that default, because a trail sharing its row with controls is
          one control among them and a 16px name would make the row two sizes tall. */}
      <ol className={cn('flex flex-wrap items-center gap-1.5', size === 'lg' ? 'text-body-lg' : 'text-body-md')}>
        {items.map((item, index) => {
          const isLast = index === items.length - 1;
          return (
            <Fragment key={item.key}>
              {index > 0 && (
                <li aria-hidden="true" className="text-neutral-8 select-none">
                  /
                </li>
              )}
              <li className="min-w-0">
                {item.pending ? (
                  // The words are still being read, but the trail must still SAY where the reader
                  // is: a bare placeholder leaves the nav announcing no current page for as long as
                  // the read takes. The type label is what the crumb would render if the read never
                  // landed, so it is the honest thing to announce meanwhile.
                  //
                  // And it must still GO there. A pending crumb mid-trail is a step the reader can
                  // still want — on `/projects/:id/reports/:reportId` the pending one is the project,
                  // which is the way back out — so pending decides what the crumb SAYS, and position
                  // decides whether it is a link, exactly as for a resolved one. Testing `pending`
                  // first and stopping there made every pending crumb inert, including that one.
                  isLast || item.to === null ? (
                    <span aria-current={isLast ? 'page' : undefined} className="flex items-center gap-1.5 min-w-0">
                      <CrumbContent item={item} />
                    </span>
                  ) : (
                    <Link
                      to={item.to}
                      className="flex items-center gap-1.5 min-w-0 text-muted hover:text-fg transition-colors no-underline"
                    >
                      <CrumbContent item={item} />
                    </Link>
                  )
                ) : isLast || item.to === null ? (
                  <span
                    aria-current={isLast ? 'page' : undefined}
                    className={cn(
                      'flex items-center gap-1.5 min-w-0',
                      isLast ? 'text-fg font-medium' : 'text-muted',
                    )}
                  >
                    <CrumbContent item={item} />
                  </span>
                ) : (
                  <Link
                    to={item.to}
                    className="flex items-center gap-1.5 min-w-0 text-muted hover:text-fg transition-colors no-underline"
                  >
                    <CrumbContent item={item} />
                  </Link>
                )}
              </li>
            </Fragment>
          );
        })}
      </ol>
    </nav>
  );
}
