import { useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { clamp, formatDuration } from '@byteshelf/plugin';
import { STEP_SECONDS } from './playerKeys';
import { frameAt, type Storyboard } from './recordedVideo';

/** The storyboard's grid, and where it is loaded from. */
export interface StoryboardImage {
  url: string;
  layout: Storyboard;
}

interface SeekBarProps {
  durationSeconds: number;
  currentSeconds: number;
  /** What the browser has of the video, each from a time to a time. */
  buffered: readonly (readonly [number, number])[];
  onSeek: (seconds: number) => void;
  storyboard: StoryboardImage | null;
}

/**
 * Where the video is and what of it is loaded, which a click or a drag moves it to, showing the storyboard's frame
 * of where the pointer is. With the focus, ← and → move it 5 seconds, which then step no file in the preview.
 */
export function SeekBar({ durationSeconds, currentSeconds, buffered, onSeek, storyboard }: SeekBarProps) {
  const bar = useRef<HTMLDivElement>(null);
  /** Where the pointer is over it, or drags it to, in seconds. */
  const [pointed, setPointed] = useState<number | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const shown = isDragging && pointed !== null ? pointed : currentSeconds;
  const share = (seconds: number) => (durationSeconds > 0 ? clamp(seconds / durationSeconds, 0, 1) * 100 : 0);

  function timeAt(event: PointerEvent): number {
    const { left, width } = bar.current!.getBoundingClientRect();
    return width > 0 ? clamp((event.clientX - left) / width, 0, 1) * durationSeconds : 0;
  }

  function seekBy(seconds: number): void {
    onSeek(clamp(currentSeconds + seconds, 0, durationSeconds));
  }

  function onKeyDown(event: KeyboardEvent): void {
    const moves: Record<string, () => void> = {
      ArrowLeft: () => seekBy(-STEP_SECONDS),
      ArrowRight: () => seekBy(STEP_SECONDS),
      Home: () => onSeek(0),
      End: () => onSeek(durationSeconds),
    };
    const move = moves[event.key];
    if (!move || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    event.preventDefault();
    move();
  }

  return (
    <div
      ref={bar}
      role="slider"
      tabIndex={0}
      aria-label="Seek"
      aria-valuemin={0}
      aria-valuemax={Math.round(durationSeconds)}
      aria-valuenow={Math.round(shown)}
      aria-valuetext={`${formatDuration(shown, { isElapsed: true })} of ${formatDuration(durationSeconds)}`}
      className="video-relative video-flex video-h-8 video-min-w-16 video-flex-1 video-cursor-pointer video-touch-none video-items-center video-rounded-full video-outline-none focus-visible:video-ring-2 focus-visible:video-ring-current"
      onKeyDown={onKeyDown}
      onPointerDown={(event) => {
        if (event.button !== 0) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        setIsDragging(true);
        setPointed(timeAt(event));
      }}
      onPointerMove={(event) => {
        if (isDragging || event.pointerType === 'mouse') setPointed(timeAt(event));
      }}
      onPointerUp={(event) => {
        if (!isDragging) return;
        setIsDragging(false);
        onSeek(timeAt(event));
        if (event.pointerType !== 'mouse') setPointed(null);
      }}
      onPointerCancel={() => {
        setIsDragging(false);
        setPointed(null);
      }}
      onPointerLeave={() => {
        if (!isDragging) setPointed(null);
      }}
    >
      <div className="video-relative video-h-1 video-w-full video-overflow-hidden video-rounded-full">
        <div className="video-absolute video-inset-0 video-bg-current video-opacity-20" />
        {buffered.map(([from, to]) => (
          <div key={from} className="video-absolute video-inset-y-0 video-bg-current video-opacity-30" style={{ left: `${share(from)}%`, width: `${share(to) - share(from)}%` }} />
        ))}
        <div className="video-absolute video-inset-y-0 video-left-0 video-bg-current" style={{ width: `${share(shown)}%` }} />
      </div>
      <div className="video-pointer-events-none video-absolute video-size-3 video-rounded-full video-bg-current" style={{ left: `${share(shown)}%`, transform: 'translateX(-50%)' }} />
      {pointed !== null && <PointedFrame seconds={pointed} share={share(pointed)} storyboard={storyboard} />}
    </div>
  );
}

/** The storyboard's frame of where the pointer is, and its time, above the bar there and within its ends. */
function PointedFrame({ seconds, share, storyboard }: { seconds: number; share: number; storyboard: StoryboardImage | null }) {
  const half = (storyboard?.layout.frameWidth ?? 0) / 2;
  return (
    <div
      className="video-pointer-events-none video-absolute video-bottom-full video-mb-3 video-flex video-flex-col video-items-center video-gap-1"
      style={{ left: `clamp(${half}px, ${share}%, calc(100% - ${half}px))`, transform: 'translateX(-50%)' }}
    >
      {storyboard && <Frame image={storyboard} seconds={seconds} />}
      <span className="video-rounded-md video-bg-black/70 video-px-1.5 video-py-0.5 video-text-body-sm video-tabular-nums video-text-white">{formatDuration(seconds, { isElapsed: true })}</span>
    </div>
  );
}

function Frame({ image: { url, layout }, seconds }: { image: StoryboardImage; seconds: number }) {
  const { x, y } = frameAt(layout, seconds);
  return (
    <div
      data-frame={`${x},${y}`}
      className="video-overflow-hidden video-rounded-md video-bg-black video-shadow-lg"
      style={{
        width: layout.frameWidth,
        height: layout.frameHeight,
        backgroundImage: `url("${url}")`,
        backgroundPosition: `-${x}px -${y}px`,
        backgroundSize: `${layout.columns * layout.frameWidth}px ${layout.rows * layout.frameHeight}px`,
      }}
    />
  );
}
