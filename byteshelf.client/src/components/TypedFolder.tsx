import { Suspense, type DragEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Icon, PageLayout, Spinner, iconMap } from '../ui';
import { useFolderContents } from '../hooks/useFolderContents';
import { useShelfPreview, type ShelfPreview, type ShownFiles } from '../hooks/useShelfPreview';
import { useInvalidateShelf, useShelf } from '../hooks/useShelfQueries';
import type { ShelfWork } from '../hooks/useShelfWork';
import type { FolderType } from '../lib/plugins/folderType';
import { folderContentsRoute } from '../lib/shelfPaths';
import type { StoreFolder } from '../services/fileStoring';
import { FilePreview } from './preview/FilePreview';
import { ShelfHeader } from './ShelfHeader';

interface TypedFolderProps {
  shelf: string;
  path: string;
  work: ShelfWork;
  /** What to show in the viewer of its type; null while that is not known, or not unlocked. */
  viewed: { type: FolderType; folder: StoreFolder } | null;
}

/** The files of a viewer's folder, of which each is previewed alone: which belong together, only the viewer knows. */
const ALONE: ShownFiles = { files: [] };

const LOADING = <PageLayout><Spinner /></PageLayout>;

/** A folder in the viewer of its type, which has everything under the breadcrumbs. What the viewer fails with is for what holds this. */
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
        {viewed
          ? <Viewer key={viewed.folder.directory.id} shelf={shelf} work={work} preview={preview} type={viewed.type} folder={viewed.folder} />
          : LOADING}
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

interface ViewerProps {
  shelf: string;
  work: ShelfWork;
  preview: ShelfPreview;
  type: FolderType;
  folder: StoreFolder;
}

function Viewer({ shelf, work: { encryption, reading }, preview, type: { component: Component }, folder }: ViewerProps) {
  const { data: info } = useShelf(shelf);
  const changed = useInvalidateShelf();
  const contents = useFolderContents({ shelf, folder, reading, store: encryption.store, changed, open: preview.open });

  return contents && (
    <Suspense fallback={LOADING}>
      <Component shelf={shelf} folder={folder.directory} contents={contents} canWrite={info?.permission === 'readWrite' && !info.onReplica} />
    </Suspense>
  );
}
