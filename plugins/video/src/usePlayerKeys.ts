import type { RefObject } from 'react';
import { isPlainKey, usePreviewKeys } from '@byteshelf/plugin';
import { keyAction } from './playerKeys';

/**
 * The player's keys (`keyAction`), of those the preview hands its viewer (`usePreviewKeys`), pressed with nothing
 * held. Held down, J and L jump again as the key repeats, and Space or K play or pause once: their repeats are taken
 * all the same, so that they scroll nothing.
 */
export function usePlayerKeys(video: RefObject<HTMLVideoElement | null>, { toggle, seek }: { toggle: () => void; seek: (seconds: number) => void }): void {
  usePreviewKeys((event) => {
    const element = video.current;
    const action = keyAction(event.key);
    if (!element || !action || !isPlainKey(event)) return;
    event.preventDefault();
    if (action.kind === 'jump') seek(element.currentTime + action.seconds);
    else if (!event.repeat) toggle();
  });
}
