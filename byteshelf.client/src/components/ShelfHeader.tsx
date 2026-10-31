import { useNavigate } from 'react-router-dom';
import { Button, ButtonGroup } from './ui';
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

/** A shelf's name, where in it the page is, its usage, and its files, trash and (for admins) settings. */
export function ShelfHeader({ shelf, view, path = ROOT_PATH }: ShelfHeaderProps) {
  const navigate = useNavigate();
  const isAdmin = useIsAdmin();
  const { data: info } = useShelf(shelf);

  const views = [
    { value: 'files', label: 'Files' },
    { value: 'trash', label: 'Trash' },
    ...(isAdmin ? [{ value: 'settings', label: 'Settings' }] : []),
  ];

  function open(next: string): void {
    navigate(shelfViewRoute(shelf, next as ShelfView));
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <nav aria-label="Breadcrumbs" className="flex flex-wrap items-center gap-1 text-heading-sm">
          <Button variant="text" onClick={() => navigate(shelfRoute(shelf))} className="!px-1 text-heading-sm text-text-1-light dark:text-text-1-dark">
            {shelf}
          </Button>
          {view === 'files' && pathSegments(path).map((segment) => (
            <span key={segment.path} className="flex items-center gap-1">
              <span className="text-text-4-light dark:text-text-4-dark">/</span>
              <Button variant="text" onClick={() => navigate(shelfRoute(shelf, segment.path))} className="!px-1 text-heading-sm text-text-1-light dark:text-text-1-dark">
                {segment.name}
              </Button>
            </span>
          ))}
        </nav>
        <ButtonGroup options={views} value={view} onChange={open} size="sm" />
      </div>
      {info && <p className="text-body-md text-text-3-light dark:text-text-3-dark">{describeUsage(info)}</p>}
    </div>
  );
}
