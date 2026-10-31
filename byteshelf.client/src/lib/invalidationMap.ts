import { queryKeys } from './queryKeys';

// Maps the server's invalidation resources to the query roots they make stale. The names must match
// ByteShelf.Server/Invalidation/Models/InvalidationResources.cs: a resource without an entry here
// silently stops refreshing (invalidationMap.test.ts checks the two lists match). An event's metadata,
// a shelf's id, is not used: refetching the few shelf queries on screen is cheap.
export const RESOURCE_TO_QUERY_KEYS: Record<string, (readonly unknown[])[]> = {
  users: [queryKeys.users.root],
  shelves: [queryKeys.shelves.root],
  'shelf-contents': [queryKeys.shelfContents.root],
  'shelf-access': [queryKeys.shelfAccess.root],
};

export const INVALIDATION_RESOURCES = Object.freeze(Object.keys(RESOURCE_TO_QUERY_KEYS));
