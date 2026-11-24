import { QueryClient } from '@tanstack/react-query';

/** How many times a failed query is asked again. */
export const QUERY_RETRIES = 1;

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: QUERY_RETRIES,
    },
  },
});
