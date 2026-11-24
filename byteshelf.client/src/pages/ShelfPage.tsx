import { useParams } from 'react-router-dom';
import { CleanupBoundary } from '../components/CleanupBoundary';
import { FolderListing } from '../components/FolderListing';
import { TypedFolder } from '../components/TypedFolder';
import { useFolderView } from '../hooks/useFolderView';
import { useShelfWork } from '../hooks/useShelfWork';
import { pathFromRoute } from '../lib/shelfPaths';

/** One directory on a shelf: in the viewer of its type, or its listing. */
export function ShelfPage() {
  // Nothing begun on one shelf goes on over another, such as a dialog of one's file or a search of it.
  const shelf = useParams().shelf!;
  return <FilesOf key={shelf} shelf={shelf} />;
}

function FilesOf({ shelf }: { shelf: string }) {
  const path = pathFromRoute(useParams()['*']);
  const work = useShelfWork(shelf);
  const view = useFolderView(shelf, path, work.passwords.folderPassword);

  // Below what is kept of the folder and the shelf, which what goes does not take with it.
  return (
    <CleanupBoundary>
      {view.as === 'listing'
        ? <FolderListing shelf={shelf} path={path} work={work} toViewer={view.toViewer} />
        : <TypedFolder shelf={shelf} path={path} work={work} viewed={view.as === 'viewer' ? view : null} />}
    </CleanupBoundary>
  );
}
