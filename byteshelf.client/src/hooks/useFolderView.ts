import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { folderSuffixOf, folderTypeOf } from '../lib/plugins/folderTypes';
import type { FolderType } from '../lib/plugins/folderType';
import { nameOf, shelfRoute, showsContents } from '../lib/shelfPaths';
import { isNotFound } from '../services/apiClient';
import type { StoreFolder } from '../services/fileStoring';
import { useFolderLock, type AskFolderPassword } from './useFolderLock';
import { usePluginHost, usePluginsAwaited } from './usePluginHost';
import { useListing } from './useShelfQueries';

/** How a folder's listing gives way to the viewer of its type: as the person chooses that, or gives the password. */
export type WayToViewer =
  | { by: 'choice'; type: FolderType; show: () => void }
  | { by: 'password'; unlock: () => void };

/** What a folder is shown as. */
export type FolderView =
  /** `toViewer` is null when no viewer would show it. */
  | { as: 'listing'; toViewer: WayToViewer | null }
  /** Not known yet, or not unlocked. */
  | { as: 'opening' }
  | { as: 'viewer'; type: FolderType; folder: StoreFolder };

/** A folder as it was opened, and the type it then had; undefined while that waits for plugins. */
interface Opened {
  path: string;
  asContents: boolean;
  type: FolderType | null | undefined;
}

/** What the folder at `path` is shown as: in the viewer of its type, once it is unlocked, unless its contents are asked for. */
export function useFolderView(shelf: string, path: string, folderPassword: AskFolderPassword): FolderView {
  const navigate = useNavigate();
  const { plugins } = usePluginHost();
  const awaitsPlugins = usePluginsAwaited();
  const [query] = useSearchParams();
  const { data: listing, error, errorUpdateCount } = useListing(shelf, path);

  const name = nameOf(path);
  const type = folderTypeOf(plugins, name);
  const asContents = showsContents(query);
  // A plugin still loading may have the type, or come first of two that do.
  const awaitsType = awaitsPlugins && folderSuffixOf(name) !== null;

  // Kept until the folder is opened again: a plugin loaded later takes nothing the person is using from them.
  const [kept, setKept] = useState<Opened | null>(null);
  const isKept = kept !== null && kept.path === path && kept.asContents === asContents && (kept.type !== undefined || awaitsType);
  const opened = isKept ? kept : { path, asContents, type: awaitsType ? undefined : type };
  if (!isKept) setKept(opened);

  function offering(show: () => void): FolderView {
    return { as: 'listing', toViewer: type && { by: 'choice', type, show } };
  }

  /** What it is shown as once it is unlocked. */
  function unlocked(): FolderView {
    if (asContents) return offering(() => navigate(shelfRoute(shelf, path)));
    if (opened.type === undefined) return { as: 'opening' };
    if (opened.type === null) return offering(() => setKept({ ...opened, type }));
    if (isNotFound(error)) return { as: 'listing', toViewer: null };
    // By whether it ever failed, as a listing that then shows asks again, and there is no error meanwhile.
    if (!listing) return errorUpdateCount > 0 ? { as: 'listing', toViewer: null } : { as: 'opening' };
    return { as: 'viewer', type: opened.type, folder: { directory: listing.directory, encryptedBy: listing.encryptedBy ?? null } };
  }

  const view = unlocked();
  const { lock, unlock } = useFolderLock(path, view.as === 'viewer' ? view.folder.encryptedBy : null, folderPassword);
  if (view.as !== 'viewer' || lock === 'unlocked') return view;
  return lock === 'opening' ? { as: 'opening' } : { as: 'listing', toViewer: { by: 'password', unlock } };
}
