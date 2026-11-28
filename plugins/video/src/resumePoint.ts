import { readStoredValue, writeStoredValue } from '@byteshelf/plugin';

/** Where in each video it was stopped, by file id, most recent last: kept in this browser alone, where it keeps anything. */
const KEY = 'byteshelf-video-positions';

/** So many are kept, the oldest going first. */
export const KEPT = 200;

/** A video is picked up where it was stopped only that far from either end: near the start it starts, near the end it was watched. */
export const MARGIN_SECONDS = 10;

/** Where to pick up a video of `durationSeconds` that was stopped at `saved`; null to start from the start. */
export function resumeAt(saved: number | null, durationSeconds: number): number | null {
  return saved !== null && saved > MARGIN_SECONDS && saved < durationSeconds - MARGIN_SECONDS ? saved : null;
}

/** Where the video of `fileId` was stopped; null when it was not, or this browser keeps nothing. */
export function savedPosition(fileId: string): number | null {
  const position = positions().find(([id]) => id === fileId)?.[1];
  return typeof position === 'number' && Number.isFinite(position) ? position : null;
}

/** Keeps that the video of `fileId` was stopped at `seconds`, or with null that it was not, as once it was watched to the end. */
export function savePosition(fileId: string, seconds: number | null): void {
  const others = positions().filter(([id]) => id !== fileId);
  const kept = seconds === null ? others : [...others, [fileId, seconds] as const];
  writeStoredValue(KEY, JSON.stringify(kept.slice(-KEPT)));
}

function positions(): (readonly [string, number])[] {
  let kept: unknown;
  try {
    kept = JSON.parse(readStoredValue(KEY) ?? '[]');
  } catch {
    return [];
  }
  return Array.isArray(kept) ? kept.filter((entry): entry is [string, number] => Array.isArray(entry) && typeof entry[0] === 'string' && typeof entry[1] === 'number') : [];
}
