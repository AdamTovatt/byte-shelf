import { useEffect, useRef, type RefObject } from 'react';
import { savePosition } from './resumePoint';

/** How often where it is is kept while it plays. */
const KEEP_EVERY_MS = 2000;

/**
 * Keeps where `video`, the video of `fileId`, is (`savePosition`), or that it was watched to the end: `keep` at once,
 * as it pauses or ends, `keepNowAndThen` at most every 2 seconds, as it plays, and by itself as it goes, as the
 * preview closes or moves on.
 */
export function useKeptPosition(video: RefObject<HTMLVideoElement | null>, fileId: string): { keep: (element: HTMLVideoElement) => void; keepNowAndThen: (element: HTMLVideoElement) => void } {
  const lastKept = useRef(0);

  useEffect(() => {
    const element = video.current;
    return () => {
      if (element && element.readyState > 0) keepPosition(fileId, element);
    };
  }, [video, fileId]);

  function keep(element: HTMLVideoElement): void {
    lastKept.current = Date.now();
    keepPosition(fileId, element);
  }

  return {
    keep,
    keepNowAndThen: (element) => {
      if (Date.now() - lastKept.current > KEEP_EVERY_MS) keep(element);
    },
  };
}

function keepPosition(fileId: string, element: HTMLVideoElement): void {
  savePosition(fileId, element.ended ? null : element.currentTime);
}
