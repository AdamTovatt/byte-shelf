import { useState, type SyntheticEvent } from 'react';
import type { Playback } from './VideoControls';

const STOPPED: Playback = { isPaused: true, currentSeconds: 0, durationSeconds: null, buffered: [], volume: { level: 1, isMuted: false }, rate: 1 };

type VideoEvent = (event: SyntheticEvent<HTMLVideoElement>) => void;

/** The handlers of a video element's events that tell where it is, which `usePlayback` spreads on it. */
export interface PlaybackEvents {
  onDurationChange: VideoEvent;
  onTimeUpdate: VideoEvent;
  onProgress: VideoEvent;
  onPlay: VideoEvent;
  onPause: VideoEvent;
  onWaiting: VideoEvent;
  onPlaying: VideoEvent;
  onSeeked: VideoEvent;
  onVolumeChange: VideoEvent;
  onRateChange: VideoEvent;
}

/**
 * Where a video is as it plays, as its element's events say (`events`), and whether it waits for what it plays;
 * `seeked` shows a seek at once as where it is, as one that waits for what it plays sends no `timeupdate`.
 */
export function usePlayback(): { playback: Playback; isWaiting: boolean; events: PlaybackEvents; seeked: (seconds: number) => void } {
  const [playback, setPlayback] = useState(STOPPED);
  const [isWaiting, setIsWaiting] = useState(false);

  function update(changed: Partial<Playback>): void {
    setPlayback((before) => ({ ...before, ...changed }));
  }

  const events: PlaybackEvents = {
    onDurationChange: ({ currentTarget }) => update({ durationSeconds: Number.isFinite(currentTarget.duration) ? currentTarget.duration : null }),
    onTimeUpdate: ({ currentTarget }) => update({ currentSeconds: currentTarget.currentTime }),
    onProgress: ({ currentTarget }) => update({ buffered: rangesOf(currentTarget.buffered) }),
    onPlay: () => update({ isPaused: false }),
    onPause: () => update({ isPaused: true }),
    onWaiting: () => setIsWaiting(true),
    onPlaying: () => setIsWaiting(false),
    onSeeked: () => setIsWaiting(false),
    onVolumeChange: ({ currentTarget }) => update({ volume: { level: currentTarget.volume, isMuted: currentTarget.muted } }),
    onRateChange: ({ currentTarget }) => update({ rate: currentTarget.playbackRate }),
  };
  return { playback, isWaiting, events, seeked: (seconds) => update({ currentSeconds: seconds }) };
}

function rangesOf(ranges: TimeRanges): (readonly [number, number])[] {
  return Array.from({ length: ranges.length }, (_, index) => [ranges.start(index), ranges.end(index)] as const);
}
