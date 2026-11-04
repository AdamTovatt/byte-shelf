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

/** The tabs of a shelf's settings, each its own route below the settings page; the first is its default. */
export const SHELF_SETTINGS_TABS = ['general', 'access', 'keys'] as const;
export type ShelfSettingsTab = (typeof SHELF_SETTINGS_TABS)[number];

function shelfBase(shelf: string): string {
  return `/shelves/${encodeURIComponent(shelf)}`;
}

/** The browser route of a directory on a shelf, each name escaped. */
export function shelfRoute(shelf: string, path: string = ROOT_PATH): string {
  const names = path.split('/').filter((name) => name.length > 0).map(encodeURIComponent);
  return names.length > 0 ? `${shelfBase(shelf)}/files/${names.join('/')}` : shelfBase(shelf);
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
