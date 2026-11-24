import { createContext, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, type RefObject } from 'react';

interface UseOverlayOptions {
  isOpen: boolean;
  onClose: () => void;
  /** Any key but Escape and, given its `element`, Tab: heard only while this overlay is the topmost, as Escape is. */
  onKeyDown?: (event: KeyboardEvent) => void;
  /** The overlay's element, which takes the focus and keeps it while the overlay is the topmost. */
  element?: RefObject<HTMLElement | null>;
}

/** Where an overlay is among those open. */
export interface OverlayPlace {
  /** What encloses the overlay's own content, for it to provide to that through {@link EnclosingOverlaysContext}. */
  enclosing: readonly symbol[];
  /** Whether no other is under it, so that its backdrop is the one that dims the page. */
  dimsPage: boolean;
  /** How many are on top of it, which it is drawn under. */
  above: number;
}

/** What Tab stops at, where it is drawn. */
const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), iframe, video[controls], audio[controls], [tabindex]:not([tabindex="-1"])';

/**
 * The overlays that enclose the subtree being rendered, outermost first: none on the page itself.
 *
 * Exported so an overlay component can scope its own children; `Modal` is the only one that does.
 */
export const EnclosingOverlaysContext = createContext<readonly symbol[]>([]);

/**
 * The place of a dialog whose overlay may be swapped for another, as one that loads is for the one loaded:
 * the next to open in it, with no other of it open, goes where the first one was, under what has opened
 * over it since.
 */
export interface OverlaySlot {
  openCount: number;
  /** Null until one has opened in it. */
  order: number | null;
}

/** The slot of the dialog being rendered: none for an overlay that a page opens itself. */
export const OverlaySlotContext = createContext<OverlaySlot | null>(null);

interface OpenOverlay {
  token: symbol;
  enclosing: readonly symbol[];
  onKeyDown: (event: KeyboardEvent) => void;
  /** When it opened, by which the open ones are sorted: its slot's, when another was open in that before. */
  order: number;
}

/**
 * Every overlay open right now, in the order they opened: the last is on top, drawn over the others, and
 * Escape is its.
 *
 * Module-level because it is a fact about the page rather than about a component: two overlays that
 * know nothing of each other have to agree on which of them Escape belongs to, on which dims the page,
 * and on who may give the page its scrollbar back.
 *
 * A `Symbol` per overlay rather than an identifier a caller supplies: nothing outside this file can name
 * an entry, so nothing can remove somebody else's.
 */
const openOverlays: OpenOverlay[] = [];

/** The order of the last overlay to open on top. */
let lastOrder = 0;

/** Told as an overlay opens or closes. */
const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Whether any overlay is open, which then has the keys. */
export function useIsOverlayOpen(): boolean {
  return useSyncExternalStore(subscribe, () => openOverlays.length > 0);
}

/** How many overlays are on top of the one of `token`, and whether none is under it; null while it is not open. */
function placeOf(token: symbol): Omit<OverlayPlace, 'enclosing'> | null {
  const index = openOverlays.findIndex((overlay) => overlay.token === token);
  return index === -1 ? null : { above: openOverlays.length - 1 - index, dimsPage: index === 0 };
}

// One listener for them all, so that a key is the one overlay's that is on top as it is pressed, and not
// also the next one's, on top once that has closed.
function handleKeyDown(event: KeyboardEvent): void {
  openOverlays.at(-1)?.onKeyDown(event);
}

/**
 * Puts an overlay on top, or back in its slot's place, and answers how to take it away. Opening order is
 * told by when its effect ran, except within one commit, whose effects React runs child-first: an overlay
 * goes under those it encloses, which can only have opened with it. Where they are in the document would
 * not tell, as an overlay and the confirmation it raised both portal into `document.body`.
 */
function open({ token, enclosing, onKeyDown }: Omit<OpenOverlay, 'order'>, slot: OverlaySlot | null): () => void {
  const inside = openOverlays.find((other) => other.enclosing.includes(token));
  const kept = slot?.openCount === 0 ? slot.order : null;
  const overlay: OpenOverlay = { token, enclosing, onKeyDown, order: inside?.order ?? kept ?? ++lastOrder };
  const over = openOverlays.findIndex((other) => other === inside || other.order > overlay.order);
  openOverlays.splice(over === -1 ? openOverlays.length : over, 0, overlay);
  if (slot) {
    slot.openCount++;
    slot.order ??= overlay.order;
  }
  if (openOverlays.length === 1) {
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', handleKeyDown);
  }
  listeners.forEach((listener) => listener());

  return () => {
    openOverlays.splice(openOverlays.indexOf(overlay), 1);
    if (slot) slot.openCount--;
    if (openOverlays.length === 0) {
      document.body.style.overflow = '';
      document.removeEventListener('keydown', handleKeyDown);
    }
    listeners.forEach((listener) => listener());
  };
}

/**
 * Keeps Tab within `element`: past its last stop it goes on at the first, and back. A focus elsewhere is
 * left alone, such as in a menu the overlay opened, which is a sibling of it in the document; one that
 * is nowhere, as after what had it is gone, comes back in.
 */
function keepTabWithin(element: HTMLElement, event: KeyboardEvent): void {
  const stops = Array.from(element.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((stop) => stop.checkVisibility());
  const active = document.activeElement;
  const within = element.contains(active);
  if (!within && active !== document.body) return;

  const index = stops.indexOf(active as HTMLElement);
  const leaves = event.shiftKey ? index <= 0 : index === stops.length - 1 || !within;
  if (!leaves) return;
  event.preventDefault();
  stops.at(event.shiftKey ? -1 : 0)?.focus();
}

/**
 * The page-level behaviour every overlay shares: the body does not scroll behind it, Escape dismisses
 * it, and, given its `element`, the focus goes into it once it is on top, stays in it, and goes back to
 * where it was before it opened as it closes, unless it went elsewhere meanwhile.
 *
 * **Escape reaches the topmost overlay only** — see {@link openOverlays}. A
 * non-dismissible overlay still takes its turn at the top and simply does nothing with it — which is the
 * point, since an Escape it refuses must not fall through to the surface underneath.
 *
 * **The scroll lock is released by the last one out, not by the first.** `overflow` is set while any
 * overlay is open and cleared only when the stack empties, so an inner overlay closing cannot hand
 * the page back its scrollbar while an outer one is still covering it.
 */
export function useOverlay({ isOpen, onClose, onKeyDown, element }: UseOverlayOptions): OverlayPlace {
  const [token] = useState(() => Symbol('overlay'));
  const enclosing = useContext(EnclosingOverlaysContext);
  const slot = useContext(OverlaySlotContext);
  const [above, setAbove] = useState(0);
  const [dimsPage, setDimsPage] = useState(true);

  // Read through refs so the effect below depends on `isOpen` alone: each is a fresh function on most
  // renders — `Modal` derives `onClose` from `dismissible` inline — and if one were a dependency, an
  // ordinary re-render of an OUTER overlay would take it away and put it back on top, stealing Escape
  // from the overlay it opened.
  const onCloseRef = useRef(onClose);
  const onKeyDownRef = useRef(onKeyDown);
  const enclosingRef = useRef(enclosing);
  const tookFocus = useRef(false);
  useLayoutEffect(() => {
    onCloseRef.current = onClose;
    onKeyDownRef.current = onKeyDown;
    enclosingRef.current = enclosing;
  });

  // Its place is told before it is painted, so that of two that open at once one is drawn over the other,
  // and one dims the page.
  useLayoutEffect(() => {
    if (!isOpen) return;
    const unsubscribe = subscribe(() => {
      const place = placeOf(token);
      if (!place) return;
      setAbove(place.above);
      setDimsPage(place.dimsPage);
    });
    const close = open({
      token,
      enclosing: enclosingRef.current,
      onKeyDown: (event) => {
        if (event.key === 'Escape') onCloseRef.current();
        else if (event.key === 'Tab' && element?.current) keepTabWithin(element.current, event);
        else onKeyDownRef.current?.(event);
      },
    }, slot);
    return () => {
      unsubscribe();
      close();
    };
  }, [isOpen, token, element, slot]);

  useEffect(() => {
    if (!isOpen || !element) return;
    const before = document.activeElement;
    return () => {
      tookFocus.current = false;
      // What had it here is gone. Anywhere else, as in an overlay that stays, it is left.
      if (document.activeElement === document.body && before instanceof HTMLElement) before.focus();
    };
  }, [isOpen, element]);

  // Once, also when it opened under another, which had the focus.
  useEffect(() => {
    if (!isOpen || tookFocus.current || placeOf(token)?.above !== 0) return;
    tookFocus.current = true;
    element?.current?.focus();
  }, [isOpen, above, token, element]);

  return {
    enclosing: useMemo(() => [...enclosing, token], [enclosing, token]),
    dimsPage,
    above,
  };
}
