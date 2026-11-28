import type { DerivedStatus } from '@byteshelf/plugin';

/** What the player plays of a video, whether the original may be chosen over the stream, and what it says of its stream. */
export interface Playing {
  /** The stream, the original, the video itself, or null when neither plays, which leaves the video to the next viewer. */
  source: 'stream' | 'original' | null;
  /** Whether the original may be played instead of the stream: while both may play. */
  canChooseOriginal: boolean;
  /** The one line said of a stream there is not yet; null when there is nothing to say. */
  note: string | null;
}

interface WhatThereIs {
  /** Whether the file's listing named a stream as the player opened it. */
  listed: boolean;
  /** Whether the reader said it streams well as it is; null while that is not known, or it said nothing. */
  streamsWell: boolean | null;
}

/** Whether to ask what there is of a stream: of one not listed, once the reader says the video does not stream well as it is. */
export function asksForStream({ listed, streamsWell }: WhatThereIs): boolean {
  return !listed && streamsWell === false;
}

/**
 * The stream when the listing names one, unless the original was chosen; else the original. Of the two, one that
 * does not play leaves the other, and when neither does there is nothing. Of a stream not listed (`asksForStream`),
 * it says only that it is on its way or that there is no room for it.
 */
export function playingOf({ listed, streamsWell, status, streamFailed, originalFailed, wantsOriginal }: WhatThereIs & {
  /** What there is of a stream asked for; null when it was not asked, or not answered. */
  status: DerivedStatus | null;
  streamFailed: boolean;
  originalFailed: boolean;
  wantsOriginal: boolean;
}): Playing {
  if (listed && !streamFailed) {
    const canChooseOriginal = !originalFailed;
    return { source: canChooseOriginal && wantsOriginal ? 'original' : 'stream', canChooseOriginal, note: null };
  }
  const note = asksForStream({ listed, streamsWell }) ? noteOf(status) : null;
  return { source: originalFailed ? null : 'original', canChooseOriginal: false, note };
}

function noteOf(status: DerivedStatus | null): string | null {
  if (status === 'making') return 'A streaming copy is on its way.';
  if (status === 'noRoom') return 'There is no room on this shelf for a streaming copy.';
  return null;
}
