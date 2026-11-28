// What plugins import as `@byteshelf/plugin`: the UI library, and what of the app they may use. All of it
// is what a plugin's `api` promises: taking away anything a release holds raises it (Plugins.md, Versions;
// index.test.ts).

export * from '../ui';
export { definePlugin, type PluginDefinition } from '../lib/plugins/pluginDefinition';
export type { FileAction, FileActionContext } from '../lib/plugins/fileAction';
export type { FolderContents, FolderType, FolderViewerProps } from '../lib/plugins/folderType';
export type { Neighbour, Viewer, ViewerProps } from '../lib/preview/viewerRegistry';
export type { ContentReader } from '../lib/content/contentReader';
export { loadDerived, type DerivedReader, type DerivedStatus } from '../lib/derived/derivedReader';
export { loadImage } from '../lib/loadImage';
export { LockedError, type LockableReader } from '../lib/content/lockableReader';
export { relativeTo } from '../lib/shelfPaths';
export { formatDuration } from '../lib/format';
export { isPlainKey } from '../lib/keys';
export { PREVIEW_BAR_BOTTOM } from '../components/preview/previewBars';
export { readStoredValue, writeStoredValue } from '../lib/storage';
export { mediaTypeOf } from '../lib/mediaTypes';
export { readText, type ReadText } from '../lib/content/readText';
export { PlainText, TextPane } from '../components/preview/TextPane';
export { BarButton, BarTextButton, PreviewControls } from '../components/preview/FloatingBars';
export { usePreviewFullScreen, usePreviewKeys, type FullScreen, type PreviewKeyHandler } from '../lib/preview/viewerPreview';
export { useLoaded, type Loaded, type Reloadable } from '../hooks/useLoaded';
export { useZoom, type Zoom } from '../hooks/useZoom';
export { DRAGS_ITSELF } from '../lib/dragsItself';
export { ErrorBoundary } from '../components/ErrorBoundary';
export { LoadedContent } from '../components/preview/LoadedContent';
export { usePluginDocuments, useSetPluginDocument, type PluginDocuments, type PluginDocumentWrite } from '../hooks/usePluginDocuments';
export type { DerivedKind, DirectoryTree, ShelfDirectory, ShelfFile } from '../services/shelfService';
export { ApiError, authorizedFetch, throwIfFailed } from '../services/apiClient';
export { invalidateResource } from '../lib/invalidate';
export type { InvalidationResource } from '../lib/invalidationMap';
export { toast } from './toast';
