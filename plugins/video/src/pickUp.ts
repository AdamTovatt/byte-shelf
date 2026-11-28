/** Where a change of what is played picks up: the time it was at, whether it was playing, its speed, and how loud. */
export interface PickUp {
  seconds: number;
  isPlaying: boolean;
  rate: number;
  volume: number;
  isMuted: boolean;
}

/** Where `element` is; before it loaded anything, where it was to pick up, `from`, as it has been nowhere itself. */
export function pickUpOf(element: HTMLVideoElement, from: PickUp | null): PickUp | null {
  return element.readyState === 0
    ? from
    : { seconds: element.currentTime, isPlaying: !element.paused, rate: element.playbackRate, volume: element.volume, isMuted: element.muted };
}

/** Has `element`, just loaded, go on as the one before it was: at its speed and volume, and playing if it was. Its time is the caller's. */
export function goOnAs(element: HTMLVideoElement, from: PickUp): void {
  element.playbackRate = from.rate;
  element.volume = from.volume;
  element.muted = from.isMuted;
  if (from.isPlaying) void element.play().catch(() => {});
}
