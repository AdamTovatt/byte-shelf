import { Suspense, useEffect, useEffectEvent, type DragEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Icon, PageLayout, Spinner, iconMap } from '../ui';
import { PluginIdentity } from '../contexts/PluginIdentity';
import { useFolderContents } from '../hooks/useFolderContents';
import { useShelfPreview, type ShelfPreview, type ShownFiles } from '../hooks/useShelfPreview';
import { useInvalidateShelf, useShelf } from '../hooks/useShelfQueries';
import type { ViewedFolder } from '../hooks/useFolderView';
import type { ShelfWork } from '../hooks/useShelfWork';
import { folderContentsRoute } from '../lib/shelfPaths';
import { folderKey } from '../services/openFolder';
import { ErrorBoundary } from './ErrorBoundary';
import { FilePreview } from './preview/FilePreview';
import { ShelfHeader } from './ShelfHeader';

interface TypedFolderProps {
  shelf: string;
  path: string;
  work: ShelfWork;
  /** What to show in the viewer of its type; null while that is not known, or not unlocked. */
  viewed: ViewedFolder | null;
}

/** The files of a viewer's folder, of which each is previewed alone: which belong together, only the viewer knows. */
const ALONE: ShownFiles = { files: [] };

const LOADING = <PageLayout><Spinner /></PageLayout>;

/** A folder in the viewer of its type, which has everything under the breadcrumbs. */
export function TypedFolder({ shelf, path, work, viewed }: TypedFolderProps) {
  const navigate = useNavigate();
  const preview = useShelfPreview(shelf, ALONE, work.reading);

  return (
    <div className="flex min-h-full flex-col" onDragOver={refuseFiles} onDrop={refuseFiles}>
      {/* Without the space below it: how far from it the viewer begins is the viewer's own. */}
      <PageLayout className="-mb-6 md:-mb-8">
        <ShelfHeader
          shelf={shelf}
          view="files"
          path={path}
          menuItem={viewed
            ? { label: 'Show contents', icon: (size) => <Icon icon={iconMap.folder} size={size} />, onChoose: () => navigate(folderContentsRoute(shelf, path)) }
            : undefined}
        />
      </PageLayout>
      <div className="flex flex-1 flex-col">
        {viewed ? (
          <ErrorBoundary key={folderKey(viewed.folder)} fallback={<Failed onFailed={viewed.onFailed} />}>
            <Viewer shelf={shelf} work={work} preview={preview} viewed={viewed} />
          </ErrorBoundary>
        ) : LOADING}
      </div>
      {preview.isOpen && <FilePreview {...preview.shown} detailsOn={shelf} actions={work.fileActions} />}
    </div>
  );
}

/** Keeps the browser from leaving the page for a file dropped on it, unless the viewer, or a field of its, takes it. */
function refuseFiles(event: DragEvent): void {
  if (event.defaultPrevented || !event.dataTransfer.types.includes('Files') || (event.target as Element).closest('input[type="file"]')) return;
  event.preventDefault();
  event.dataTransfer.dropEffect = 'none';
}

/** In place of a viewer that failed, until that is known. */
function Failed({ onFailed }: { onFailed: () => void }) {
  const tell = useEffectEvent(onFailed);
  useEffect(() => tell(), []);
  return null;
}

interface ViewerProps {
  shelf: string;
  work: ShelfWork;
  preview: ShelfPreview;
  viewed: ViewedFolder;
}

function Viewer({ shelf, work: { reading, store }, preview, viewed: { pluginId, type: { component: Component }, folder } }: ViewerProps) {
  const { data: info } = useShelf(shelf);
  const changed = useInvalidateShelf();
  const contents = useFolderContents({ shelf, folder, reading, store, changed, open: preview.open });

  return contents && (
    <Suspense fallback={LOADING}>
      <PluginIdentity id={pluginId}>
        <Component shelf={shelf} folder={folder.directory} contents={contents} canWrite={info?.permission === 'readWrite' && !info.onReplica} />
      </PluginIdentity>
    </Suspense>
  );
}
