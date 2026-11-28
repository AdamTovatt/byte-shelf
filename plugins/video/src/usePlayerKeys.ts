import { useEffect, type RefObject } from 'react';
import { keyAction } from './playerKeys';

/** The player's keys (`keyAction`), taken from anywhere in the preview the video is in, but a field or a dialog over it. */
export function usePlayerKeys(video: RefObject<HTMLVideoElement | null>, { toggle, seek }: { toggle: () => void; seek: (seconds: number) => void }): void {
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      const element = video.current;
      if (!element || event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
      const preview = element.closest('[role="dialog"]');
      const isInPreview = event.target === document.body || (event.target instanceof Node && preview?.contains(event.target) === true);
      const action = isInPreview ? keyAction(event.key, event.target) : null;
      if (!action) return;
      event.preventDefault();
      if (action.kind === 'toggle') toggle();
      else seek(element.currentTime + action.seconds);
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  });
}
