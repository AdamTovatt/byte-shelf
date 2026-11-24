import type { SearchCriteria } from './searchFilters';

// Hierarchical TanStack Query keys, one root per invalidation resource. Hooks build their queries
// from these and lib/invalidationMap.ts targets the same roots, so the live updates and the hooks
// cannot drift apart. Invalidating a root covers every key under it.
export const queryKeys = {
  users: {
    root: ['users'] as const,
    me: ['users', 'me'] as const,
    list: ['users', 'list'] as const,
    passkeys: ['users', 'me', 'passkeys'] as const,
  },
  shelves: {
    root: ['shelves'] as const,
    list: ['shelves', 'list'] as const,
    info: (shelf: string) => ['shelves', 'info', shelf] as const,
  },
  shelfContents: {
    root: ['shelfContents'] as const,
    listing: (shelf: string, path: string) => ['shelfContents', shelf, 'listing', path] as const,
    tree: (shelf: string, path: string) => ['shelfContents', shelf, 'tree', path] as const,
    file: (shelf: string, id: string) => ['shelfContents', shelf, 'file', id] as const,
    deletion: (shelf: string, deletionId: string) => ['shelfContents', shelf, 'deletion', deletionId] as const,
    trash: (shelf: string) => ['shelfContents', shelf, 'trash'] as const,
    search: (shelf: string, under: string, criteria: SearchCriteria) => ['shelfContents', shelf, 'search', under, criteria] as const,
    duplicates: (shelf: string, under: string, criteria: SearchCriteria) => ['shelfContents', shelf, 'duplicates', under, criteria] as const,
    metadata: (shelf: string, id: string) => ['shelfContents', shelf, 'metadata', id] as const,
    // By the shelf's id, which a file carries and no rename changes; the other keys have its name.
    pluginDocuments: (shelfId: string, id: string) => ['shelfContents', shelfId, 'pluginDocuments', id] as const,
  },
  shelfAccess: {
    root: ['shelfAccess'] as const,
    grants: (shelf: string) => ['shelfAccess', shelf, 'grants'] as const,
    keys: (shelf: string) => ['shelfAccess', shelf, 'keys'] as const,
    // Upper case, which no shelf name has, so it never meets a shelf's keys above.
    keyGrants: (keyId: string) => ['shelfAccess', 'keyGrants', keyId] as const,
  },
  // Not an invalidation resource: links are each server's own and not in the change log, so this
  // app's own changes refresh them.
  links: {
    root: ['links'] as const,
    list: (shelf: string) => ['links', shelf] as const,
  },
  // Not an invalidation resource: replication state is not in the change log, so it is polled.
  replication: {
    status: ['replication', 'status'] as const,
  },
  // Not an invalidation resource: which plugins there are changes only with a restart of the server, and what
  // is wrong with a reader, which may change while it runs, is seen the next time they are listed.
  plugins: {
    list: ['plugins'] as const,
  },
  // Not an invalidation resource either: the running build's version is polled, and its release
  // changes only with a deploy, which the poll offers a reload for.
  version: {
    current: ['version', 'current'] as const,
    release: ['version', 'release'] as const,
  },
} as const;
