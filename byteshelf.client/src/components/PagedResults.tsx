import type { ReactNode } from 'react';
import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query';
import { Button, EmptyState, Spinner } from '../ui';
import { LoadError } from './LoadError';

interface PagedResultsProps<Page, Item> {
  /** The search, fetched a page at a time. */
  query: UseInfiniteQueryResult<InfiniteData<Page>, Error>;
  /** The items one page holds. */
  itemsOf: (page: Page) => Item[];
  /** Shown when the search fails without a message of its own. */
  failure: string;
  /** Shown when the search finds nothing. */
  empty: string;
  /** Spacing between the items' block and the Show more button below it. */
  className: string;
  /** Draws every item of the pages fetched so far. */
  children: (items: Item[]) => ReactNode;
}

/**
 * A search's results a page at a time: its error, a spinner while the first page loads, what it says
 * when nothing is found, else the items and, while more follow, a Show more button that adds the next page.
 */
export function PagedResults<Page, Item>({ query, itemsOf, failure, empty, className, children }: PagedResultsProps<Page, Item>) {
  if (query.error) return <LoadError error={query.error} failure={failure} />;
  if (query.isLoading || !query.data) return <Spinner />;
  const items = query.data.pages.flatMap(itemsOf);
  if (items.length === 0) return <EmptyState message={empty} />;

  return (
    <div className={className}>
      {children(items)}
      {query.hasNextPage && <Button variant="secondary" onClick={() => void query.fetchNextPage()} loading={query.isFetchingNextPage}>Show more</Button>}
    </div>
  );
}
