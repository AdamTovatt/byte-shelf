import type { ComponentType } from 'react';
import type { ShelfFile } from '../../services/shelfService';
import { UNTYPED, mediaTypeOf } from '../mediaTypes';
import type { ContentReader } from './contentReader';

/** What a viewer's component is given: the file's record and its contents, whatever they are read from. */
export interface ViewerProps {
  file: ShelfFile;
  content: ContentReader;
}

/** Shows files of some types in the preview. */
export interface Viewer {
  id: string;
  /** The media types it takes, without parameters; one may be a family, such as `image/*`. */
  mediaTypes: readonly string[];
  /** The name extensions it takes, such as `.md`, of files stored without a type (`application/octet-stream`). */
  extensions: readonly string[];
  component: ComponentType<ViewerProps>;
}

/** How closely a viewer takes a file: not at all, by its type's family, or by its type or extension. */
const MATCH = { none: 0, family: 1, exact: 2 } as const;

/**
 * The viewers the preview shows files with, and which one shows a file: the one that takes its exact
 * type (or, untyped, its extension) before one that takes its type's family, then a plugin's before a
 * built-in one, then the plugin whose id comes first.
 */
export class ViewerRegistry {
  private readonly registered: { viewer: Viewer; pluginId: string | null }[] = [];

  /** Adds the viewers of the plugin `pluginId`, or the built-in ones for null. */
  register(pluginId: string | null, viewers: readonly Viewer[]): void {
    this.registered.push(...viewers.map((viewer) => ({ viewer, pluginId })));
  }

  /** The viewer that shows `file`; null when none takes it. */
  viewerFor(file: ShelfFile): Viewer | null {
    const taking = this.registered
      .map((each) => ({ ...each, match: matchOf(each.viewer, file) }))
      .filter((each) => each.match !== MATCH.none);
    // The sort is stable, so between a plugin's own viewers the first it registered wins.
    taking.sort((one, other) => other.match - one.match || byPlugin(one.pluginId, other.pluginId));
    return taking[0]?.viewer ?? null;
  }
}

function matchOf(viewer: Viewer, file: ShelfFile): number {
  const type = mediaTypeOf(file.contentType);
  const name = file.name.toLowerCase();
  if (type === UNTYPED && viewer.extensions.some((extension) => name.endsWith(extension))) return MATCH.exact;
  if (viewer.mediaTypes.includes(type)) return MATCH.exact;
  return viewer.mediaTypes.includes(`${type.split('/')[0]}/*`) ? MATCH.family : MATCH.none;
}

function byPlugin(one: string | null, other: string | null): number {
  if (one === other) return 0;
  if (one === null) return 1;
  if (other === null) return -1;
  return one < other ? -1 : 1;
}
