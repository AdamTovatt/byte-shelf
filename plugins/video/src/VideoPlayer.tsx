import { useEffect, useRef, useState, type SyntheticEvent } from 'react';
import { LoadedContent, Spinner, clamp, useLoaded, usePluginDocuments, usePreviewFullScreen, type DerivedKind, type DerivedReader, type DerivedStatus, type ViewerProps } from '@byteshelf/plugin';
import { asksForStream, playingOf } from './playing';
import { recordedVideo } from './recordedVideo';
import { resumeAt, savePosition, savedPosition } from './resumePoint';
import type { StoryboardImage } from './SeekBar';
import { usePlayerKeys } from './usePlayerKeys';
import { VideoControls, type Playback } from './VideoControls';

/** How often where it is is kept while it plays. */
const SAVE_EVERY_MS = 2000;

/** `MediaError`'s codes: what was loading was stopped as asked, or the network failed it, either of which does not say it cannot play. */
const MEDIA_ERR_ABORTED = 1;
const MEDIA_ERR_NETWORK = 2;

/** A video in a player of the app's own, in the preview's bars. An encrypted one is the next viewer's, as one it cannot play is. */
export function VideoPlayer(props: ViewerProps) {
  const { file, Fallback } = props;
  return file.encrypted ? <Fallback /> : <Player {...props} />;
}

/** Where a change of what is played picks up: the time it was at, whether it was playing, and at what speed. */
interface Continued {
  seconds: number;
  isPlaying: boolean;
  rate: number;
}

/** What is played of the file, as it was chosen and found, and where the video played next picks up. */
interface Choice {
  wantsOriginal: boolean;
  streamFailed: boolean;
  originalFailed: boolean;
  /** Where the one before it was; null to start as a video opened does. */
  from: Continued | null;
  /** How often it was loaded again, so that each load is a video of its own. */
  attempt: number;
}

const OPENED: Choice = { wantsOriginal: false, streamFailed: false, originalFailed: false, from: null, attempt: 0 };

/**
 * The stream when the listing names one, else the video itself, which it falls back to when the stream does not
 * play (`playingOf`); with the poster until it plays, the storyboard over the seek bar, and where it was stopped
 * picked up.
 */
function Player({ file, content, derived, Fallback }: ViewerProps) {
  const recorded = recordedVideo(usePluginDocuments(file).data?.contents);
  // As the listing was when it opened: a stream made while the original plays is not switched to under it.
  const [listed] = useState(() => derived.has('stream'));
  const [choice, setChoice] = useState(OPENED);
  const streamsWell = recorded?.streamsWell ?? null;
  const asked = useLoaded(asksForStream({ listed, streamsWell }) ? derived : null, (signal) => statusOf(derived, signal));
  const playing = playingOf({ listed, streamsWell, status: asked.status === 'done' ? asked.value : null, ...choice });
  const url = useLoaded(playing.source, (signal) => (playing.source === 'stream' ? derived.url('stream', signal) : content.url(signal)));
  const poster = useMadeUrl(derived, 'thumbnail');
  const storyboardUrl = useMadeUrl(derived, 'preview');
  const storyboard: StoryboardImage | null = storyboardUrl && recorded?.storyboard ? { url: storyboardUrl, layout: recorded.storyboard } : null;

  function change(changed: Partial<Choice>): void {
    setChoice((before) => ({ ...before, ...changed }));
  }

  // A stream listed that is gone since is played no more.
  if (playing.source === 'stream' && url.status === 'done' && url.value === null) change({ streamFailed: true });
  if (playing.source === null) return <Fallback />;

  return (
    <LoadedContent loaded={url}>
      {(src) => src && (
        <Video
          key={`${choice.attempt} ${src}`}
          src={src}
          fileId={file.id}
          name={file.name}
          poster={poster}
          storyboard={storyboard}
          note={playing.note}
          from={choice.from}
          original={playing.canChooseOriginal ? { isPlayed: playing.source === 'original', toggle: (from) => change({ wantsOriginal: playing.source !== 'original', from }) } : null}
          onFailed={(from) => change(playing.source === 'stream' ? { streamFailed: true, from } : { originalFailed: true, from })}
          onReload={(from) => change({ attempt: choice.attempt + 1, from })}
        />
      )}
    </LoadedContent>
  );
}

interface VideoProps {
  src: string;
  fileId: string;
  name: string;
  poster: string | null;
  storyboard: StoryboardImage | null;
  note: string | null;
  /** Where the video before this one of the file was, picked up once this one is loaded; null when there was none. */
  from: Continued | null;
  /** Playing the original rather than the stream, and switching, picking up where this one is, while both may be played. */
  original: { isPlayed: boolean; toggle: (from: Continued | null) => void } | null;
  /** This one cannot play, which the next takes up from where it is. */
  onFailed: (from: Continued | null) => void;
  /** This one is to be loaded again, from where it is, once the network failed it. */
  onReload: (from: Continued | null) => void;
}

const STOPPED: Playback = { isPaused: true, currentSeconds: 0, durationSeconds: 0, buffered: [], volume: { level: 1, isMuted: false }, rate: 1 };

/** The video element, what it says of where it is, and its controls (`VideoControls`). */
function Video({ src, fileId, name, poster, storyboard, note, from, original, onFailed, onReload }: VideoProps) {
  const video = useRef<HTMLVideoElement>(null);
  const [playback, setPlayback] = useState(STOPPED);
  const [isWaiting, setIsWaiting] = useState(false);
  const [isCutOff, setIsCutOff] = useState(false);
  const lastSaved = useRef(0);
  const pressedWith = useRef('mouse');
  const fullScreen = usePreviewFullScreen(video);

  function update(changed: Partial<Playback>): void {
    setPlayback((before) => ({ ...before, ...changed }));
  }

  /** Plays or pauses it; cut off by the network, it is loaded again and plays on from where it was. */
  function toggle(): void {
    const element = video.current;
    if (!element) return;
    if (isCutOff) {
      const at = continuedFrom(element, from);
      onReload(at && { ...at, isPlaying: true });
    } else if (element.paused) void element.play().catch(() => {});
    else element.pause();
  }

  /** Shown at once as where it is, so that a seek waiting for what it plays, which sends no `timeupdate`, is where the next moves on from. */
  function seek(seconds: number): void {
    const element = video.current;
    if (!element) return;
    element.currentTime = clamp(seconds, 0, Number.isFinite(element.duration) ? element.duration : Infinity);
    update({ currentSeconds: element.currentTime });
  }

  function save(element: HTMLVideoElement): void {
    lastSaved.current = Date.now();
    keepPosition(fileId, element);
  }

  usePlayerKeys(video, { toggle, seek });

  // Where it is is kept as it goes, as the preview closes or moves on.
  useEffect(() => {
    const element = video.current;
    return () => {
      if (element && element.readyState > 0) keepPosition(fileId, element);
    };
  }, [fileId]);

  function onLoadedMetadata(event: SyntheticEvent<HTMLVideoElement>): void {
    const element = event.currentTarget;
    const at = from ? from.seconds : resumeAt(savedPosition(fileId), element.duration);
    if (at !== null) element.currentTime = at;
    if (from) element.playbackRate = from.rate;
    if (from?.isPlaying) void element.play().catch(() => {});
  }

  function onError(event: SyntheticEvent<HTMLVideoElement>): void {
    const element = event.currentTarget;
    const code = element.error?.code;
    if (code === MEDIA_ERR_NETWORK) setIsCutOff(true);
    else if (code !== MEDIA_ERR_ABORTED) onFailed(continuedFrom(element, from));
  }

  const shownNote = isCutOff ? 'The video stopped loading. Play it to load it again.' : note;

  return (
    <div className="video-relative video-flex video-min-h-0 video-flex-1 video-items-center video-justify-center video-bg-black">
      <video
        ref={video}
        src={src}
        poster={poster ?? undefined}
        aria-label={name}
        playsInline
        preload="metadata"
        className="video-max-h-full video-max-w-full"
        onPointerDown={(event) => {
          pressedWith.current = event.pointerType;
        }}
        // A click plays or pauses it; a tap is the bars', showing or hiding them.
        onClick={() => pressedWith.current === 'mouse' && toggle()}
        onDoubleClick={() => fullScreen.canFullScreen && fullScreen.toggle()}
        onLoadedMetadata={onLoadedMetadata}
        onDurationChange={(event) => update({ durationSeconds: Number.isFinite(event.currentTarget.duration) ? event.currentTarget.duration : 0 })}
        onTimeUpdate={(event) => {
          update({ currentSeconds: event.currentTarget.currentTime });
          if (Date.now() - lastSaved.current > SAVE_EVERY_MS) save(event.currentTarget);
        }}
        onProgress={(event) => update({ buffered: rangesOf(event.currentTarget.buffered) })}
        onPlay={() => update({ isPaused: false })}
        onPause={(event) => {
          update({ isPaused: true });
          save(event.currentTarget);
        }}
        onEnded={(event) => save(event.currentTarget)}
        onWaiting={() => setIsWaiting(true)}
        onPlaying={() => setIsWaiting(false)}
        onSeeked={() => setIsWaiting(false)}
        onVolumeChange={(event) => update({ volume: { level: event.currentTarget.volume, isMuted: event.currentTarget.muted } })}
        onRateChange={(event) => update({ rate: event.currentTarget.playbackRate })}
        onError={onError}
      />
      {isWaiting && !playback.isPaused && !isCutOff && <div className="video-pointer-events-none video-absolute"><Spinner size="lg" /></div>}
      {shownNote && (
        <p className="video-pointer-events-none video-absolute video-inset-x-0 video-mx-auto video-w-fit video-rounded-full video-bg-black/60 video-px-3 video-py-1 video-text-body-sm video-text-white" style={{ bottom: 'calc(var(--preview-bar-bottom, 0px) + 8px)' }}>
          {shownNote}
        </p>
      )}
      <VideoControls
        video={video}
        playback={playback}
        storyboard={storyboard}
        toggle={toggle}
        seek={seek}
        original={original && { isPlayed: original.isPlayed, toggle: () => video.current && original.toggle(continuedFrom(video.current, from)) }}
        fullScreen={fullScreen}
      />
    </div>
  );
}

/** The URL of what was made of the video of `kind` when the listing names it; null until then, and when there is none. */
function useMadeUrl(derived: DerivedReader, kind: DerivedKind): string | null {
  const loaded = useLoaded(derived.has(kind) ? kind : null, (signal) => derived.url(kind, signal));
  return loaded.status === 'done' ? loaded.value : null;
}

/** What there is of the video's stream; null when the asking failed, which is then said nothing of. */
async function statusOf(derived: DerivedReader, signal: AbortSignal): Promise<DerivedStatus | null> {
  try {
    return await derived.status('stream', signal);
  } catch {
    return null;
  }
}

/** Where `element` is; before it loaded anything, where it was to pick up, `from`, as it has been nowhere itself. */
function continuedFrom(element: HTMLVideoElement, from: Continued | null): Continued | null {
  return element.readyState === 0 ? from : { seconds: element.currentTime, isPlaying: !element.paused, rate: element.playbackRate };
}

/** Keeps where `element`, the video of `fileId`, is, or that it was watched to the end. */
function keepPosition(fileId: string, element: HTMLVideoElement): void {
  savePosition(fileId, element.ended ? null : element.currentTime);
}

function rangesOf(ranges: TimeRanges): (readonly [number, number])[] {
  return Array.from({ length: ranges.length }, (_, index) => [ranges.start(index), ranges.end(index)] as const);
}
