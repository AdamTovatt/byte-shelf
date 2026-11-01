import { useCallback, useEffect, useState } from 'react';

/**
 * The keyframe whose end means the outline has finished leaving.
 *
 * A CSS identifier held in TypeScript, which is exactly the drift the timing below refuses to accept
 * — so it is not left to a comment: `useBusyOutlineLifecycle.test.ts` reads `styles.css` and fails if
 * the stylesheet no longer defines a keyframe by this name. Renaming it there without renaming it
 * here would otherwise leave the outline waiting for an `animationend` that never comes, visible only
 * as a stroke that lingers for the fallback second and then blinks out.
 */
export const BUSY_OUTLINE_SHRINK_ANIMATION = 'busy-outline-shrink';

/**
 * A ceiling on how long a leaving outline may stay mounted, for the case where its `animationend`
 * never arrives — the element was hidden, the animation was suppressed, a test environment lays
 * nothing out. Deliberately far longer than the exit really takes: it is a safety net, not the
 * timing, so it carries none of the drift risk a duration duplicated from the stylesheet would.
 */
const EXIT_FALLBACK_MS = 1_000;

/** Nothing on screen; arriving or holding; leaving. */
type Phase = 'idle' | 'active' | 'exiting';

export interface BusyOutlineLifecycle {
  /** Whether to render the outline at all — true while active, and on through its exit. */
  render: boolean;
  /** Whether it is currently leaving, which is what selects the exit animation. */
  exiting: boolean;
  /**
   * Wire to the animated element's `onAnimationEnd`.
   *
   * Typed by the one field it reads rather than as React's `AnimationEvent`, which is what makes it
   * callable from a test without building a synthetic event. It still assigns to a real handler slot:
   * a function accepting less is usable wherever one accepting more is expected, so this goes into
   * `BusyOutline`'s DOM-typed prop unchanged — the reverse would not.
   */
  onAnimationEnd: (event: { animationName: string }) => void;
}

/**
 * Keeps a `BusyOutline` mounted long enough to play its exit.
 *
 * Without this the outline would vanish the instant the work finished, since it is rendered
 * conditionally — and it is rendered conditionally on purpose: the layer carries a `ResizeObserver`,
 * so mounting one behind every button in the app to hold an exit that almost never runs is the wrong
 * trade (see `Squircle`, which makes the same point about not blanketing the measurement).
 *
 * **The end of the exit is read from the animation, not from a clock.** A duration copied into
 * TypeScript would be a second spelling of a number that lives in `styles.css`, free to drift from
 * it — and the failure is quiet either way: unmount early and the stroke is clipped mid-shrink,
 * unmount late and a leftover dot rides on around the edge. `animationend` is exact and cannot
 * disagree with the stylesheet. The timeout beside it exists only so a suppressed animation cannot
 * strand the layer forever, which is why it is an order of magnitude longer than the real exit
 * rather than equal to it.
 *
 * **One phase value, and every transition made by a pure updater.** This began as two booleans with
 * the exit armed from inside a `setState` updater, which React requires to be pure — under
 * `StrictMode` the updater runs twice, so the fallback timer was scheduled twice and only the second
 * handle was kept. The orphan then fired a second later and dropped the outline mid-work, with no way
 * back until `busy` toggled again. Arming lives in an effect keyed on the phase now, so a double
 * invocation is a re-run with a cleanup rather than a leak.
 */
export function useBusyOutlineLifecycle(active: boolean): BusyOutlineLifecycle {
  const [phase, setPhase] = useState<Phase>(active ? 'active' : 'idle');

  useEffect(() => {
    // Pure updaters: they read the current phase and return the next, and do nothing else.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- syncing the phase to the `active` prop
    setPhase((current) => {
      if (active) return 'active';
      // Guarded on what is already on screen, so the first render of an inactive outline starts no
      // exit for something that never arrived.
      return current === 'idle' ? 'idle' : 'exiting';
    });
  }, [active]);

  useEffect(() => {
    if (phase !== 'exiting') return;
    const timer = window.setTimeout(() => setPhase('idle'), EXIT_FALLBACK_MS);
    return () => window.clearTimeout(timer);
  }, [phase]);

  const onAnimationEnd = useCallback((event: { animationName: string }) => {
    // Named, because more than one animation ends on this element: the emerge does too, and an
    // unfiltered handler would unmount the outline the moment it finished arriving.
    if (event.animationName === BUSY_OUTLINE_SHRINK_ANIMATION) setPhase('idle');
  }, []);

  return { render: phase !== 'idle', exiting: phase === 'exiting', onAnimationEnd };
}
