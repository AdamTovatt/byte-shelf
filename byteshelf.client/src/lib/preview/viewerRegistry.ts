import type { ComponentType } from 'react';
import type { ShelfFile } from '../../services/shelfService';
import { UNTYPED, mediaTypeOf } from '../mediaTypes';
import type { ContentReader } from '../content/contentReader';
import { logFailureOnce } from '../logFailureOnce';

/** What a viewer's component is given: the file's record and its contents, whatever they are read from. */
export interface ViewerProps {
  file: ShelfFile;
  content: ContentReader;
}

/** Shows files of some types in the preview. Types and extensions are compared whatever their case. */
export interface Viewer {
  id: string;
  /** The media types it takes, without parameters; one may be a family, such as `image/*`. */
  mediaTypes: readonly string[];
  /** The name extensions it takes, such as `.md`, of files stored without a type (`application/octet-stream`). */
  extensions: readonly string[];
  component: ComponentType<ViewerProps>;
  /** Why this browser cannot show files with it now; null when it can, as one without this always can. */
  unavailable?: () => string | null;
}

/** The viewer that shows a file and the plugin it came with, null for a built-in one, or why none of those that take the file's type can in this browser. */
export type ViewerMatch = { viewer: Viewer; pluginId: string | null; unavailable: null } | { viewer: null; unavailable: string };

/** How closely a viewer takes a file: not at all, by its type's family, or by its type or extension. */
const MATCH = { none: 0, family: 1, exact: 2 } as const;

/** A viewer, the types and extensions it takes as files' are compared, and the plugin it came with, null for a built-in one. */
interface Registered {
  viewer: Viewer;
  mediaTypes: readonly string[];
  extensions: readonly string[];
  pluginId: string | null;
}

/**
 * The viewers the preview shows files with, and which one shows a file: the one that takes its exact type
 * (or, untyped, its extension) before one that takes its type's family, then a plugin's before a built-in
 * one, then the plugin whose id comes first. One that is unavailable is passed over. One registry never
 * changes; another holds more.
 */
export class ViewerRegistry {
  static readonly EMPTY = new ViewerRegistry([]);

  private readonly registered: readonly Registered[];

  private constructor(registered: readonly Registered[]) {
    this.registered = registered;
  }

  /** This registry with the viewers of the plugin `pluginId`, or the built-in ones for null, as well. */
  with(pluginId: string | null, viewers: readonly Viewer[]): ViewerRegistry {
    const added = viewers.map((viewer) => ({ viewer, mediaTypes: viewer.mediaTypes.map(folded), extensions: viewer.extensions.map(folded), pluginId }));
    return new ViewerRegistry([...this.registered, ...added]);
  }

  /** The viewer that shows `file`, or why those that take it cannot; null when none takes it. */
  viewerFor(file: ShelfFile): ViewerMatch | null {
    const taking = this.registered
      .map((each) => ({ ...each, match: matchOf(each, file) }))
      .filter((each) => each.match !== MATCH.none);
    // The sort is stable, so between a plugin's own viewers the first it registered wins.
    taking.sort((one, other) => other.match - one.match || byPlugin(one.pluginId, other.pluginId));

    let first: ViewerMatch | null = null;
    for (const { viewer, pluginId } of taking) {
      const unavailable = whyUnavailable(viewer);
      if (unavailable === null) return { viewer, pluginId, unavailable };
      first ??= { viewer: null, unavailable };
    }
    return first;
  }
}

/** Why a viewer is unavailable; one that cannot say, a plugin's that fails, is. */
function whyUnavailable(viewer: Viewer): string | null {
  try {
    return viewer.unavailable?.() ?? null;
  } catch (caught) {
    logFailureOnce(viewer, `The viewer ${viewer.id} could not say whether it is available.`, caught);
    return 'The viewer failed.';
  }
}

function folded(text: string): string {
  return text.toLowerCase();
}

function matchOf({ mediaTypes, extensions }: Registered, file: ShelfFile): number {
  const type = mediaTypeOf(file.contentType);
  const name = folded(file.name);
  if (type === UNTYPED && extensions.some((extension) => name.endsWith(extension))) return MATCH.exact;
  if (mediaTypes.includes(type)) return MATCH.exact;
  return mediaTypes.includes(`${type.split('/')[0]}/*`) ? MATCH.family : MATCH.none;
}

function byPlugin(one: string | null, other: string | null): number {
  if (one === other) return 0;
  if (one === null) return 1;
  if (other === null) return -1;
  return one < other ? -1 : 1;
}
