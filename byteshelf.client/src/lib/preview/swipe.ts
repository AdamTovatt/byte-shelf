import { createContext } from 'react';
import type { Point } from './imageZoom';

/** What a swipe of one finger does. */
export interface Swipes {
  /** The finger went left: what comes next. */
  onLeft?: (() => void) | null;
  /** The finger went right: what came before. */
  onRight?: (() => void) | null;
  /** The finger went down: away, as a close button does. */
  onDown?: (() => void) | null;
}

/** A way a swipe goes, by what it does. */
type Way = keyof Swipes;

/** What a swipe goes along: across, to either side, or down. */
export type Axis = 'across' | 'down';

/** What a swipe `way` goes along. */
export function axisOf(way: Way): Axis {
  return way === 'onDown' ? 'down' : 'across';
}

/** How far a finger goes each way for a swipe, in pixels. */
const DISTANCE: Record<Way, number> = { onLeft: 50, onRight: 50, onDown: 100 };

/** How many times further a swipe goes its way than the other. */
const MIN_SLANT = 1.5;

/**
 * The swipe a finger made, if any: its whole `travel` is one, as far and as slanted as a swipe goes, and `counted`
 * goes as far that way too. Zoomed in, what counts is what of the travel the image did not follow since it last did,
 * so a swipe is one the image could not follow; otherwise all of it counts. Up is none.
 */
export function swipeOf(travel: Point, counted: Point = travel): Way | null {
  const way = wayOf(travel);
  return way && along(way, travel) >= DISTANCE[way] && along(way, counted) >= DISTANCE[way] ? way : null;
}

/** The way `travel` slants: across, or down. */
function wayOf({ x, y }: Point): Way | null {
  if (Math.abs(x) >= Math.abs(y) * MIN_SLANT) return x < 0 ? 'onLeft' : 'onRight';
  if (y >= Math.abs(x) * MIN_SLANT) return 'onDown';
  return null;
}

/** How far `point` goes `way`. */
function along(way: Way, { x, y }: Point): number {
  return way === 'onLeft' ? -x : way === 'onRight' ? x : y;
}

/**
 * What a swipe on something zoomed in does, once it goes past what the image follows (`useZoom`): the preview's
 * moves to the file beside or closes. Null where such a swipe does nothing.
 */
export const ZoomedSwipe = createContext<Swipes | null>(null);
