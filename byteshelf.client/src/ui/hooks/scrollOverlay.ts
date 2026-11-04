/**
 * Shared core of the overlay scrollbars — the two design constants and the single-axis handle
 * geometry that `useScrollbarOverlay` (two-axis, decorates an externally-owned scroll element) and
 * `useVerticalScrollOverlay` (vertical-only, owns its scroll container) both derive their handles
 * from.
 *
 * Deliberately stateless and single-axis: the caller measures its own viewport and content, works
 * out how long its track is (top offsets, and in the two-axis case the corner gap where the two
 * tracks would overlap), and owns its own event paths. Handle size is cheap and depends only on
 * those measurements, so callers recompute it rather than caching a copy that can go stale.
 *
 * "Handle" is the element each consumer positions and sizes — `custom-scroll-handle`,
 * `overflow-scroll-handle`, `scrollbar-overlay-handle-v` and `scrollbar-overlay-handle-h`. The pill
 * painted inside it is the thumb — the `ScrollThumb` component, shared by all three components —
 * which carries no geometry of its own.
 */
import { clamp } from '../lib/utils';

/** Smallest handle we render, so it stays grabbable however long the content is */
const MIN_HANDLE_SIZE = 38;

/** How long the scrollbar stays revealed after the last scroll / pointer activity */
export const FADE_OUT_DELAY_MS = 1500;

/**
 * Present on a drawn overlay track while its {@link FADE_OUT_DELAY_MS} hide timer is pending, and on
 * no track otherwise.
 *
 * It exists for something outside the app: the e2e capture harness waits for no element to carry it
 * before the shutter opens, so a track can never be photographed partway to hidden. The
 * `*-track-visible` classes cannot answer that question, because they cannot tell a track that is
 * about to fade from one meant to stay up — a `persistent` track, or one under a pointer a shot
 * deliberately rests in a scroll container.
 *
 * Driven by `useOverlayHideTimer`, the one timer both overlay hooks share: set where the hide is
 * scheduled and cleared where it fires or is cancelled, never by a second timer that could disagree
 * with the fade it reports.
 */
export const OVERLAY_HIDE_PENDING_ATTRIBUTE = 'data-overlay-hide-pending';

/** The attribute props a track spreads on: {@link OVERLAY_HIDE_PENDING_ATTRIBUTE} while a hide is pending */
export function overlayHidePendingAttributes(isHidePending: boolean): Record<string, string> {
  return isHidePending ? { [OVERLAY_HIDE_PENDING_ATTRIBUTE]: '' } : {};
}

/** The measured sizes one scrollbar axis is derived from */
export interface ScrollAxisGeometry {
  /** Visible extent of the scroll container along this axis (`clientHeight` / `clientWidth`) */
  viewSize: number;
  /** Total content extent along this axis (`scrollHeight` / `scrollWidth`) */
  contentSize: number;
  /** Usable length of the rendered track — the caller has already applied its own offsets */
  trackLength: number;
}

interface HandleMetrics {
  /** Length of the handle along the axis */
  handleSize: number;
  /** Offset of the handle from the start of the track */
  handlePos: number;
}

/** A track offset larger than the viewport leaves no track at all, not a negative one */
function usableTrackLength(trackLength: number): number {
  return Math.max(0, trackLength);
}

/** How long the handle is on this axis */
export function computeHandleSize(geometry: ScrollAxisGeometry): number {
  const { viewSize, contentSize } = geometry;
  const trackLength = usableTrackLength(geometry.trackLength);

  // An unmeasured container reports a zero content size; treat it as "everything is visible"
  // rather than dividing by zero into a NaN handle.
  const visibleRatio = contentSize > 0 ? viewSize / contentSize : 1;

  // The minimum keeps the handle grabbable, but never at the cost of overflowing a track shorter
  // than the minimum — that would give a negative maximum position and hang the handle off the
  // start of its own track.
  return Math.min(trackLength, Math.max(MIN_HANDLE_SIZE, trackLength * visibleRatio));
}

/** How far the handle can travel before it reaches the end of the track */
function maxHandlePosition(geometry: ScrollAxisGeometry): number {
  return usableTrackLength(geometry.trackLength) - computeHandleSize(geometry);
}

/** Size and position of the handle representing `scrollPos` on this axis */
export function computeHandleMetrics(geometry: ScrollAxisGeometry, scrollPos: number): HandleMetrics {
  const handleSize = computeHandleSize(geometry);
  const maxScroll = geometry.contentSize - geometry.viewSize;

  // Elastic overscroll reports a scroll offset outside the scrollable range; the handle should sit
  // at the end of its track for that, not past it.
  const scrollFraction = clamp(maxScroll > 0 ? scrollPos / maxScroll : 0, 0, 1);

  return {
    handleSize,
    handlePos: scrollFraction * (usableTrackLength(geometry.trackLength) - handleSize),
  };
}

/**
 * Where the handle should jump to for a track click at `clickPos`, or `null` if the click landed on
 * the handle itself and there is nothing to do. A click past either end travels one handle length
 * that way; overshooting the track is clamped away by {@link scrollForHandlePosition}.
 */
export function handlePositionForTrackClick(clickPos: number, handlePos: number, handleSize: number): number | null {
  if (clickPos > handlePos + handleSize) {
    return handlePos + handleSize;
  }
  if (clickPos < handlePos) {
    return handlePos - handleSize;
  }
  return null;
}

/**
 * The scroll offset a handle dropped at `candidateHandlePos` represents, or `null` if the handle
 * has nowhere to travel. The candidate is clamped into the track first, so callers can pass a raw
 * drag delta or jump target unbounded.
 */
export function scrollForHandlePosition(geometry: ScrollAxisGeometry, candidateHandlePos: number): number | null {
  const maxHandlePos = maxHandlePosition(geometry);

  // A handle that fills its track carries no position information — every offset means the same
  // thing. Reporting 0 would be a fabricated answer that snaps the content to the top, so say so
  // and let the caller leave the scroll offset alone.
  if (maxHandlePos <= 0) {
    return null;
  }

  const handlePos = clamp(candidateHandlePos, 0, maxHandlePos);
  const maxScroll = geometry.contentSize - geometry.viewSize;

  return (handlePos / maxHandlePos) * maxScroll;
}
