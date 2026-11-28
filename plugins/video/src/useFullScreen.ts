import { useEffect, useState, type RefObject } from 'react';

/** Whether a video can go full screen, whether it is, and going there or back. */
export interface FullScreen {
  canFullScreen: boolean;
  isFullScreen: boolean;
  toggle: () => void;
}

/**
 * Full screen of the preview the video is in, with its bars, where the browser can make an element full screen; else,
 * as on an iPhone, of the video alone in the browser's own player. A browser that refuses is left as it is.
 */
export function useFullScreen(video: RefObject<HTMLVideoElement | null>): FullScreen {
  const [isFullScreen, setIsFullScreen] = useState(false);

  useEffect(() => {
    const onChange = () => setIsFullScreen(document.fullscreenElement !== null);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  function toggle(): void {
    const element = video.current;
    if (!element) return;
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => {});
    else if (document.fullscreenEnabled) void (element.closest('[role="dialog"]') ?? element).requestFullscreen().catch(() => {});
    else (element as { webkitEnterFullscreen?: () => void }).webkitEnterFullscreen?.();
  }

  const canFullScreen = document.fullscreenEnabled || 'webkitEnterFullscreen' in HTMLVideoElement.prototype;
  return { canFullScreen, isFullScreen, toggle };
}
