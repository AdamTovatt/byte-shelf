// Paths on a shelf are written as the server writes them: a leading '/', names separated by '/'.
// The server normalizes and checks every path; these helpers only build and take paths apart.

export const ROOT_PATH = '/';

export function joinPath(directory: string, name: string): string {
  return directory === ROOT_PATH ? `/${name}` : `${directory}/${name}`;
}

export function parentPath(path: string): string {
  const cut = path.lastIndexOf('/');
  return cut <= 0 ? ROOT_PATH : path.slice(0, cut);
}

/** A path below `directory` as it reads from there: `2026/march.pdf` from `/invoices`; a path elsewhere stays whole. */
export function relativeTo(directory: string, path: string): string {
  const prefix = directory === ROOT_PATH ? ROOT_PATH : `${directory}/`;
  return path.startsWith(prefix) ? path.slice(prefix.length) : path;
}

export function nameOf(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

/** Orders names as the server lists a folder's entries: by their characters' code points, the order of their UTF-8 bytes. */
export function compareNames(a: string, b: string): number {
  for (let index = 0; index < a.length && index < b.length; index++) {
    const left = a.codePointAt(index)!;
    const right = b.codePointAt(index)!;
    if (left !== right) return left - right;
    // A character past the Basic Multilingual Plane takes two places.
    if (left > 0xffff) index++;
  }
  return a.length - b.length;
}

/** Each directory from the root down to `path`, for breadcrumbs: [{ name, path }]. */
export function pathSegments(path: string): { name: string; path: string }[] {
  const names = path.split('/').filter((name) => name.length > 0);
  return names.map((name, index) => ({ name, path: `/${names.slice(0, index + 1).join('/')}` }));
}

/** A shelf's pages: its files (the shelf's own route shows the root directory), its trash and its settings. */
export type ShelfView = 'files' | 'trash' | 'settings';

/**
 * The route patterns App serves a shelf's pages at; shelfRoute, shelfViewRoute and shelfSettingsRoute
 * build URLs that match them. The settings page reads its tab from `:tab`, one of SHELF_SETTINGS_TABS.
 */
export const SHELF_ROUTE_PATTERNS = {
  shelf: '/shelves/:shelf',
  files: '/shelves/:shelf/files/*',
  trash: '/shelves/:shelf/trash',
  settings: '/shelves/:shelf/settings/:tab?',
} as const;

/**
 * The tabs of a shelf's settings, each its own route below the settings page; the first one a user
 * sees is its default. Only Links is for everyone; the rest are an admin's.
 */
export const SHELF_SETTINGS_TABS = ['general', 'access', 'keys', 'links'] as const;
export type ShelfSettingsTab = (typeof SHELF_SETTINGS_TABS)[number];

function shelfBase(shelf: string): string {
  return `/shelves/${encodeURIComponent(shelf)}`;
}

/** The browser route of a directory on a shelf, each name escaped. */
export function shelfRoute(shelf: string, path: string = ROOT_PATH): string {
  const names = path.split('/').filter((name) => name.length > 0).map(encodeURIComponent);
  return names.length > 0 ? `${shelfBase(shelf)}/files/${names.join('/')}` : shelfBase(shelf);
}

const VIEW_PARAM = 'view';
const LISTING_VIEW = 'contents';

/** The browser route of a folder's own listing, where its type has a viewer that shows it otherwise. */
export function folderListingRoute(shelf: string, path: string): string {
  return `${shelfRoute(shelf, path)}?${VIEW_PARAM}=${LISTING_VIEW}`;
}

/** Whether a folder's route asks for its own listing, as `folderListingRoute` does. */
export function asksForListing(query: URLSearchParams): boolean {
  return query.get(VIEW_PARAM) === LISTING_VIEW;
}

/** The browser route of one of a shelf's pages; its files open at the root directory. */
export function shelfViewRoute(shelf: string, view: ShelfView): string {
  return view === 'files' ? shelfBase(shelf) : `${shelfBase(shelf)}/${view}`;
}

/** The browser route of one tab of a shelf's settings. */
export function shelfSettingsRoute(shelf: string, tab: ShelfSettingsTab): string {
  return `${shelfViewRoute(shelf, 'settings')}/${tab}`;
}

/** The shelf path a route's splat names: the part after /files/, which the router has already unescaped. */
export function pathFromRoute(splat: string | undefined): string {
  const names = (splat ?? '').split('/').filter((name) => name.length > 0);
  return names.length === 0 ? ROOT_PATH : `/${names.join('/')}`;
}
