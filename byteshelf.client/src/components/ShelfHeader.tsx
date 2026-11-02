import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Breadcrumbs, Dropdown, DropdownItem, IconButton, iconMap, type BreadcrumbItem } from '../ui';
import { useShelf } from '../hooks/useShelfQueries';
import { useIsAdmin } from '../hooks/useUserQueries';
import { describeUsage } from '../lib/format';
import { pathSegments, ROOT_PATH, shelfRoute, shelfViewRoute, type ShelfView } from '../lib/shelfPaths';

interface ShelfHeaderProps {
  shelf: string;
  view: ShelfView;
  /** The directory shown, for the breadcrumbs. */
  path?: string;
  /** Wraps the crumb of each folder in the trail, the shelf's root included, such as to drop onto it. */
  wrapFolderCrumb?: (folderPath: string, crumb: ReactNode) => ReactNode;
}

const VIEW_LABELS: Record<ShelfView, string> = { files: 'Files', trash: 'Trash', settings: 'Settings' };

/**
 * Where in the shelf the page is, a menu to its files, trash and (for admins) settings with the
 * current one ticked, and under them how much the shelf holds.
 */
export function ShelfHeader({ shelf, view, path = ROOT_PATH, wrapFolderCrumb }: ShelfHeaderProps) {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const { data: info } = useShelf(shelf);

  const views: ShelfView[] = isAdmin ? ['files', 'trash', 'settings'] : ['files', 'trash'];
  const crumbs: BreadcrumbItem[] = [
    // Too narrow for the whole trail on one line: first Shelves, then the shelf, show only their icons.
    { key: 'shelves', label: 'Shelves', to: '/', icon: iconMap.shelves, collapseOrder: 1 },
    { key: shelf, label: shelf, to: shelfRoute(shelf), icon: iconMap.shelf, collapseOrder: 2 },
    ...(view === 'files'
      ? pathSegments(path).map((segment): BreadcrumbItem => ({ key: segment.path, label: segment.name, to: shelfRoute(shelf, segment.path), icon: iconMap.folder }))
      : [{ key: view, label: VIEW_LABELS[view], to: null }]),
  ];
  // Which folder each crumb names: the shelf's is its root, each later one its own path.
  const folderOf = new Map<string, string>(view === 'files' ? [[shelf, ROOT_PATH], ...pathSegments(path).map((segment): [string, string] => [segment.path, segment.path])] : []);

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between gap-3">
        {/* The space beside the menu, whatever the trail's own width, which is what it collapses to fit. */}
        <div className="min-w-0 flex-1">
          <Breadcrumbs
            items={crumbs}
            wrapCrumb={wrapFolderCrumb && ((item, crumb) => {
              const folder = folderOf.get(item.key);
              return folder === undefined ? crumb : wrapFolderCrumb(folder, crumb);
            })}
          />
        </div>
        <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label={`Pages of ${shelf}`} />}>
          {views.map((value) => (
            <DropdownItem key={value} checked={value === view} onClick={() => navigate(shelfViewRoute(shelf, value))}>
              {VIEW_LABELS[value]}
            </DropdownItem>
          ))}
        </Dropdown>
      </div>
      {info && <p className="text-body-sm text-muted">{describeUsage(info)}</p>}
    </div>
  );
}
