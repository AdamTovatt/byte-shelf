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
}

const VIEW_LABELS: Record<ShelfView, string> = { files: 'Files', trash: 'Trash', settings: 'Settings' };

/**
 * Where in the shelf the page is, a menu to its files, trash and (for admins) settings with the
 * current one ticked, and under them how much the shelf holds.
 */
export function ShelfHeader({ shelf, view, path = ROOT_PATH }: ShelfHeaderProps) {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const { data: info } = useShelf(shelf);

  const views: ShelfView[] = isAdmin ? ['files', 'trash', 'settings'] : ['files', 'trash'];
  const crumbs: BreadcrumbItem[] = [
    { key: 'shelves', label: 'Shelves', to: '/' },
    { key: shelf, label: shelf, to: shelfRoute(shelf), icon: iconMap.shelf },
    ...(view === 'files'
      ? pathSegments(path).map((segment): BreadcrumbItem => ({ key: segment.path, label: segment.name, to: shelfRoute(shelf, segment.path) }))
      : [{ key: view, label: VIEW_LABELS[view], to: null }]),
  ];

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <Breadcrumbs items={crumbs} />
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
