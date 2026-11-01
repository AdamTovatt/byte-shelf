import { useNavigate } from 'react-router-dom';
import { Breadcrumbs, ButtonGroup, iconMap, type BreadcrumbItem } from '../ui';
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

/** A shelf's name, where in it the page is, its usage, and its files, trash and (for admins) settings. */
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
    <div className="space-y-3">
      <Breadcrumbs items={crumbs} />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <ButtonGroup
          options={views.map((value) => ({ value, label: VIEW_LABELS[value] }))}
          value={view}
          onChange={(next) => navigate(shelfViewRoute(shelf, next))}
          size="sm"
          ariaTablist
          ariaLabel={`${shelf} views`}
        />
        {info && <p className="text-body-md text-muted">{describeUsage(info)}</p>}
      </div>
    </div>
  );
}
