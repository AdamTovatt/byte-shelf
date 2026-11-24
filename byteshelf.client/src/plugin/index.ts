// What plugins import as `@byteshelf/plugin`: the UI library, and what of the app they may use. All of it
// is what a plugin's `api` promises: taking away anything a release holds raises it (Plugins.md, Versions;
// index.test.ts).

export * from '../ui';
export { definePlugin, type PluginDefinition } from '../lib/plugins/pluginDefinition';
export type { FileAction, FileActionContext } from '../lib/plugins/fileAction';
export type { FolderContents, FolderType, FolderViewerProps } from '../lib/plugins/folderType';
export type { Neighbour, Viewer, ViewerProps } from '../lib/preview/viewerRegistry';
export type { ContentReader } from '../lib/content/contentReader';
export type { DerivedReader } from '../lib/derived/derivedReader';
export { LockedError, type LockableReader } from '../lib/content/lockableReader';
export { relativeTo } from '../lib/shelfPaths';
export { readText, type ReadText } from '../lib/content/readText';
export { PlainText, TextPane } from '../components/preview/TextPane';
export { useLoaded, type Loaded, type Reloadable } from '../hooks/useLoaded';
export { ErrorBoundary } from '../components/ErrorBoundary';
export { LoadedContent } from '../components/preview/LoadedContent';
export { usePluginDocuments, useSetPluginDocument, type PluginDocuments, type PluginDocumentWrite } from '../hooks/usePluginDocuments';
export type { DerivedKind, DirectoryTree, ShelfDirectory, ShelfFile } from '../services/shelfService';
export { ApiError, authorizedFetch, throwIfFailed } from '../services/apiClient';
export { invalidateResource } from '../lib/invalidate';
export type { InvalidationResource } from '../lib/invalidationMap';
export { toast } from './toast';
