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

/** The key of the crumb naming the folder at `path`, the shelf's own crumb for its root. */
function folderCrumbKey(path: string): string {
  return `folder:${path}`;
}

/** The folder a crumb's key names; null for a crumb that is no folder. */
function folderOfCrumbKey(key: string): string | null {
  return key.startsWith('folder:') ? key.slice('folder:'.length) : null;
}

/**
 * Where in the shelf the page is, a menu to its files, trash and (for admins) settings with the
 * current one ticked, and under them how much the shelf holds.
 */
export function ShelfHeader({ shelf, view, path = ROOT_PATH, wrapFolderCrumb }: ShelfHeaderProps) {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const { data: info } = useShelf(shelf);

  const views: ShelfView[] = isAdmin ? ['files', 'trash', 'settings'] : ['files', 'trash'];
  // Each crumb's key says what kind of crumb it is, so a shelf or folder named like another crumb
  // (a shelf called "shelves" or "trash") never shares its key.
  const crumbs: BreadcrumbItem[] = [
    // Too narrow for the whole trail on one line: first Shelves, then the shelf, show only their icons.
    { key: 'page:shelves', label: 'Shelves', to: '/', icon: iconMap.shelves, collapseOrder: 1 },
    { key: folderCrumbKey(ROOT_PATH), label: shelf, to: shelfRoute(shelf), icon: iconMap.shelf, collapseOrder: 2 },
    ...(view === 'files'
      ? pathSegments(path).map((segment): BreadcrumbItem => ({ key: folderCrumbKey(segment.path), label: segment.name, to: shelfRoute(shelf, segment.path), icon: iconMap.folder }))
      : [{ key: `view:${view}`, label: VIEW_LABELS[view], to: null }]),
  ];

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between gap-3">
        {/* The space beside the menu, whatever the trail's own width, which is what it collapses to fit. */}
        <div className="min-w-0 flex-1">
          <Breadcrumbs
            items={crumbs}
            // Only the files' trail names folders to drop into: the shelf's crumb its root, each
            // later one its own path.
            wrapCrumb={wrapFolderCrumb && view === 'files' ? (item, crumb) => {
              const folder = folderOfCrumbKey(item.key);
              return folder === null ? crumb : wrapFolderCrumb(folder, crumb);
            } : undefined}
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
