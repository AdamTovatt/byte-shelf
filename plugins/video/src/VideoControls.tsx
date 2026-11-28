import type { RefObject } from 'react';
import { BarButton, BarTextButton, PreviewControls, formatDuration, iconMap, type FullScreen } from '@byteshelf/plugin';
import { SeekBar, type StoryboardImage } from './SeekBar';

/** The speeds a click on the speed goes through, from the one played at; from any other, the first. */
const RATES = [1, 1.25, 1.5, 2, 0.5, 0.75];

/** Where a video is as it plays, as its element last said. */
export interface Playback {
  isPaused: boolean;
  currentSeconds: number;
  /** Null while how long it plays is not known, as of a recording a browser made. */
  durationSeconds: number | null;
  /** What the browser has of it, each from a time to a time. */
  buffered: readonly (readonly [number, number])[];
  volume: { level: number; isMuted: boolean };
  rate: number;
}

interface VideoControlsProps {
  video: RefObject<HTMLVideoElement | null>;
  playback: Playback;
  storyboard: StoryboardImage | null;
  toggle: () => void;
  seek: (seconds: number) => void;
  /** Whether the original is played rather than the stream, and its button's click, while both may be played. */
  original: { isPlayed: boolean; onClick: () => void } | null;
  fullScreen: FullScreen;
}

/**
 * The player's controls, in the preview's bottom bar: play and pause, the time and the whole, the seek bar, mute and
 * the volume, the speed, the original, and full screen.
 */
export function VideoControls({ video, playback, storyboard, toggle, seek, original, fullScreen }: VideoControlsProps) {
  const { isPaused, currentSeconds, durationSeconds, buffered, volume, rate } = playback;
  const isMuted = volume.isMuted || volume.level === 0;

  function withVideo(change: (element: HTMLVideoElement) => void): void {
    if (video.current) change(video.current);
  }

  return (
    <PreviewControls>
      <BarButton icon={isPaused ? iconMap.play : iconMap.pause} aria-label={isPaused ? 'Play' : 'Pause'} title={isPaused ? 'Play (K)' : 'Pause (K)'} onClick={toggle} />
      <span className="video-whitespace-nowrap video-px-1 video-text-body-sm video-tabular-nums video-text-muted">
        {formatDuration(currentSeconds, { isElapsed: true })}
        {/* Of whole seconds as the time gone by is, so that it reads the same at the end. */}
        {durationSeconds !== null && ` / ${formatDuration(durationSeconds, { isElapsed: true })}`}
      </span>
      <SeekBar durationSeconds={durationSeconds} currentSeconds={currentSeconds} buffered={buffered} onSeek={seek} storyboard={storyboard} />
      <BarButton
        icon={isMuted ? iconMap.muted : iconMap.volume}
        aria-label={isMuted ? 'Unmute' : 'Mute'}
        title={isMuted ? 'Unmute' : 'Mute'}
        // Unmuted at no volume, it is heard at the whole.
        onClick={() => withVideo((element) => {
          element.muted = !isMuted;
          if (!element.muted && element.volume === 0) element.volume = 1;
        })}
      />
      <input
        type="range"
        aria-label="Volume"
        min={0}
        max={1}
        step={0.05}
        value={isMuted ? 0 : volume.level}
        className="video-hidden video-w-20 video-accent-current sm:video-block"
        onChange={(event) => {
          const level = Number(event.currentTarget.value);
          withVideo((element) => {
            element.volume = level;
            element.muted = level === 0;
          });
        }}
      />
      <BarTextButton label={`Speed ${rate}×`} onClick={() => withVideo((element) => {
        element.playbackRate = RATES[(RATES.indexOf(element.playbackRate) + 1) % RATES.length] ?? 1;
      })}>
        {`${rate}×`}
      </BarTextButton>
      {original && <BarTextButton label="Original" isPressed={original.isPlayed} onClick={original.onClick}>Original</BarTextButton>}
      {fullScreen.canFullScreen && (
        <BarButton
          icon={fullScreen.isFullScreen ? iconMap.exitFullScreen : iconMap.fullScreen}
          aria-label={fullScreen.isFullScreen ? 'Exit full screen' : 'Full screen'}
          title={fullScreen.isFullScreen ? 'Exit full screen' : 'Full screen'}
          onClick={fullScreen.toggle}
        />
      )}
    </PreviewControls>
  );
}
