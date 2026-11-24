import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { folderSuffixOf, folderTypeOf, type FoundFolderType } from '../lib/plugins/folderTypes';
import type { FolderType } from '../lib/plugins/folderType';
import { nameOf, shelfRoute, showsContents } from '../lib/shelfPaths';
import { isNotFound } from '../services/apiClient';
import type { OpenFolder } from '../services/openFolder';
import type { ShelfPasswords } from '../services/shelfPasswords';
import { useFolderLock } from './useFolderLock';
import { usePluginHost, usePluginsAwaited } from './usePluginHost';
import { useListing } from './useShelfQueries';

/** How a folder's listing gives way to the viewer of its type: as the person chooses that, or gives the password. */
export type WayToViewer =
  | { by: 'choice'; type: FolderType; show: () => void }
  | { by: 'password'; unlock: () => void };

/** A folder in the viewer of its type. */
export interface ViewedFolder extends FoundFolderType {
  folder: OpenFolder;
  /** To be told that the viewer failed, which leaves the listing. */
  onFailed: () => void;
}

/** What a folder is shown as. */
export type FolderView =
  /** `toViewer` is null when no viewer would show it. */
  | { as: 'listing'; toViewer: WayToViewer | null }
  /** Not known yet, or not unlocked. */
  | { as: 'opening' }
  | ({ as: 'viewer' } & ViewedFolder);

/** The opening of a folder. */
interface Opened {
  path: string;
  asContents: boolean;
  /** The type it then had; undefined while that waits for plugins. */
  found: FoundFolderType | null | undefined;
  /** The id of the folder whose viewer failed. */
  failedOn: string | null;
}

/** What the folder at `path` is shown as: in the viewer of its type, once it is unlocked, unless its contents are asked for. */
export function useFolderView(shelf: string, path: string, folderPassword: ShelfPasswords['folderPassword']): FolderView {
  const navigate = useNavigate();
  const { plugins } = usePluginHost();
  const awaitsPlugins = usePluginsAwaited();
  const [query] = useSearchParams();
  const { data: listing, error, errorUpdateCount } = useListing(shelf, path);

  const name = nameOf(path);
  const found = folderTypeOf(plugins, name);
  const asContents = showsContents(query);
  // A plugin still loading may have the type, or come first of two that do.
  const awaitsType = awaitsPlugins && folderSuffixOf(name) !== null;

  // Kept until the folder is opened again: a plugin loaded later takes nothing the person is using from them,
  // nor does a viewer that failed, whatever becomes of the folder's encryption.
  const [kept, setKept] = useState<Opened | null>(null);
  const isKept = kept !== null && kept.path === path && kept.asContents === asContents && (kept.found !== undefined || awaitsType);
  const opened = isKept ? kept : { path, asContents, found: awaitsType ? undefined : found, failedOn: null };
  if (!isKept) setKept(opened);

  function offering(show: () => void): FolderView {
    return { as: 'listing', toViewer: found && { by: 'choice', type: found.type, show } };
  }

  /** What it is shown as once it is unlocked. */
  function unlocked(): FolderView {
    if (asContents) return offering(() => navigate(shelfRoute(shelf, path)));
    if (opened.found === undefined) return { as: 'opening' };
    if (opened.found === null || (listing && opened.failedOn === listing.directory.id)) return offering(() => setKept({ ...opened, found, failedOn: null }));
    if (isNotFound(error)) return { as: 'listing', toViewer: null };
    // By whether it ever failed, as a listing that then shows asks again, and there is no error meanwhile.
    if (!listing) return errorUpdateCount > 0 ? { as: 'listing', toViewer: null } : { as: 'opening' };
    const folder = { directory: listing.directory, encryptedBy: listing.encryptedBy ?? null };
    return { as: 'viewer', ...opened.found, folder, onFailed: () => setKept({ ...opened, failedOn: folder.directory.id }) };
  }

  const view = unlocked();
  const { lock, unlock } = useFolderLock(view.as === 'viewer' ? view.folder : null, folderPassword);
  if (view.as !== 'viewer' || lock === 'unlocked') return view;
  return lock === 'opening' ? { as: 'opening' } : { as: 'listing', toViewer: { by: 'password', unlock } };
}
