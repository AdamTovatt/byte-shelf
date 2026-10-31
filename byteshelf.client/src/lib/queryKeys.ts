// Hierarchical TanStack Query keys, one root per invalidation resource. Hooks build their queries
// from these and lib/invalidationMap.ts targets the same roots, so the live updates and the hooks
// cannot drift apart. Invalidating a root covers every key under it.
export const queryKeys = {
  users: {
    root: ['users'] as const,
    me: ['users', 'me'] as const,
    list: ['users', 'list'] as const,
  },
  shelves: {
    root: ['shelves'] as const,
    list: ['shelves', 'list'] as const,
    info: (shelf: string) => ['shelves', 'info', shelf] as const,
  },
  shelfContents: {
    root: ['shelfContents'] as const,
    listing: (shelf: string, path: string) => ['shelfContents', shelf, 'listing', path] as const,
    trash: (shelf: string) => ['shelfContents', shelf, 'trash'] as const,
  },
  shelfAccess: {
    root: ['shelfAccess'] as const,
    grants: (shelf: string) => ['shelfAccess', shelf, 'grants'] as const,
    keys: (shelf: string) => ['shelfAccess', shelf, 'keys'] as const,
  },
} as const;
