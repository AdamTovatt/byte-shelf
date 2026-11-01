import { matchRoutes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import {
  joinPath,
  nameOf,
  parentPath,
  pathFromRoute,
  pathSegments,
  relativeTo,
  SHELF_ROUTE_PATTERNS,
  shelfRoute,
  shelfViewRoute,
  type ShelfView,
} from './shelfPaths';

/** The pattern a URL matches among the shelf pages, and its params, as the router hands them to a page. */
function match(url: string) {
  const routes = Object.entries(SHELF_ROUTE_PATTERNS).map(([id, path]) => ({ id, path }));
  const [matched] = matchRoutes(routes, url) ?? [];
  return matched ? { id: matched.route.id, params: matched.params } : null;
}

describe('shelf paths', () => {
  it('joins a name to the root or a directory', () => {
    expect(joinPath('/', 'a.txt')).toBe('/a.txt');
    expect(joinPath('/docs', 'a.txt')).toBe('/docs/a.txt');
  });

  it('takes a path apart into its parent and name', () => {
    expect(parentPath('/a.txt')).toBe('/');
    expect(parentPath('/docs/2026/a.txt')).toBe('/docs/2026');
    expect(parentPath('/')).toBe('/');
    expect(nameOf('/docs/2026/a.txt')).toBe('a.txt');
  });

  it('reads a path below a directory from there, and leaves one elsewhere whole', () => {
    expect(relativeTo('/', '/docs/a.txt')).toBe('docs/a.txt');
    expect(relativeTo('/docs', '/docs/2026/a.txt')).toBe('2026/a.txt');
    expect(relativeTo('/docs', '/docsextra/a.txt')).toBe('/docsextra/a.txt');
  });

  it('lists the directories down to a path', () => {
    expect(pathSegments('/')).toEqual([]);
    expect(pathSegments('/docs/2026')).toEqual([
      { name: 'docs', path: '/docs' },
      { name: '2026', path: '/docs/2026' },
    ]);
  });

  it('round-trips a path with characters a URL escapes through the router', () => {
    const path = '/rä ksmörgås/50% #1?/a%20b&c';

    const matched = match(shelfRoute('my docs', path));

    expect(matched?.id).toBe('files');
    expect(matched?.params.shelf).toBe('my docs');
    expect(pathFromRoute(matched?.params['*'])).toBe(path);
  });

  it('routes the root to the shelf itself', () => {
    expect(match(shelfRoute('docs'))?.id).toBe('shelf');
    expect(pathFromRoute(undefined)).toBe('/');
  });

  it.each<ShelfView>(['trash', 'settings'])('routes a shelf\'s %s to its page', (view) => {
    const matched = match(shelfViewRoute('50% docs', view));

    expect(matched?.id).toBe(view);
    expect(matched?.params.shelf).toBe('50% docs');
  });
});
