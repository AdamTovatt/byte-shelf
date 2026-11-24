import { useMemo } from 'react';
import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query';
import type { ShelfFile } from '../services/shelfService';
import type { ShownFiles } from './useShelfPreview';

/** The files a search found so far, `filesOf` a page of it, and how the preview gets the next. */
export function useFoundFiles<Page>(search: UseInfiniteQueryResult<InfiniteData<Page>, Error>, filesOf: (page: Page) => ShelfFile[]): ShownFiles {
  const { data, hasNextPage, fetchNextPage } = search;
  const files = useMemo(() => data?.pages.flatMap(filesOf) ?? [], [data, filesOf]);
  if (!hasNextPage) return { files };

  return {
    files,
    fetchMore: async () => {
      // Joins a fetch under way rather than starting it over, so one press of a key does not undo the last.
      const fetched = await fetchNextPage({ cancelRefetch: false, throwOnError: true });
      return { files: fetched.data?.pages.flatMap(filesOf) ?? [], more: fetched.hasNextPage };
    },
  };
}
