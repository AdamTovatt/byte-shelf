import type { RefObject } from 'react';
import { usePreviewKeys } from '@byteshelf/plugin';
import { keyAction } from './playerKeys';

/** The player's keys (`keyAction`), of those the preview hands its viewer (`usePreviewKeys`), pressed with nothing held. */
export function usePlayerKeys(video: RefObject<HTMLVideoElement | null>, { toggle, seek }: { toggle: () => void; seek: (seconds: number) => void }): void {
  usePreviewKeys((event) => {
    const element = video.current;
    if (!element || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const action = keyAction(event.key, event.target);
    if (!action) return;
    event.preventDefault();
    if (action.kind === 'toggle') toggle();
    else seek(element.currentTime + action.seconds);
  });
}
