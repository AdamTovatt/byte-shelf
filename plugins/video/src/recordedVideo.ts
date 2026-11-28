import { clamp } from '@byteshelf/plugin';

/** How the plugin's storyboard, a video's `preview`, is laid out: frames of a size, so many to a row, left to right and then down. */
export interface Storyboard {
  /** Frame `i` stands for the time from `i × intervalSeconds` to the next. */
  intervalSeconds: number;
  columns: number;
  rows: number;
  frameWidth: number;
  frameHeight: number;
  count: number;
}

/**
 * What the player takes of what the plugin's reader recorded of a video. The document says its length and size as
 * well, for whatever else reads it; the player has those of the video as it loads.
 */
export interface RecordedVideo {
  /** Whether it streams well as it is, so that no `stream` is made of it. */
  streamsWell: boolean;
  /** How its storyboard is laid out; null when the document says nothing of one that can be read. */
  storyboard: Storyboard | null;
}

/** What `document`, the reader's about a file's contents, says of a video; null when there is none or it says something else. */
export function recordedVideo(document: Record<string, unknown> | null | undefined): RecordedVideo | null {
  const { streamsWell, storyboard } = document ?? {};
  return typeof streamsWell === 'boolean' ? { streamsWell, storyboard: storyboardOf(storyboard) } : null;
}

/** Where in the storyboard the frame for `seconds` is, in pixels from its top left. */
export function frameAt(storyboard: Storyboard, seconds: number): { x: number; y: number } {
  const frame = clamp(Math.floor(seconds / storyboard.intervalSeconds), 0, storyboard.count - 1);
  return { x: (frame % storyboard.columns) * storyboard.frameWidth, y: Math.floor(frame / storyboard.columns) * storyboard.frameHeight };
}

function storyboardOf(value: unknown): Storyboard | null {
  if (typeof value !== 'object' || value === null) return null;
  const { intervalSeconds, columns, rows, frameWidth, frameHeight, count } = value as Record<string, unknown>;
  if (![intervalSeconds, columns, rows, frameWidth, frameHeight, count].every(isPositive)) return null;
  return { intervalSeconds, columns, rows, frameWidth, frameHeight, count } as Storyboard;
}

function isPositive(value: unknown): value is number {
  return typeof value === 'number' && value > 0;
}
