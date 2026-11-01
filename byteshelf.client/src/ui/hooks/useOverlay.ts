import { createContext, useContext, useEffect, useRef } from 'react';

interface UseOverlayOptions {
  isOpen: boolean;
  onClose: () => void;
}

/**
 * How many overlays enclose the subtree being rendered — 0 on the page itself, 1 inside a dialog, 2
 * inside a confirmation raised from that dialog.
 *
 * Exported so an overlay component can scope its own children; `Modal` is the only one that does.
 * A component that renders an overlay's content without providing this leaves anything nested in it
 * registering at its own depth, which reads as a sibling.
 */
export const OverlayDepthContext = createContext(0);

interface OpenOverlay {
  token: symbol;
  depth: number;
}

/**
 * Every overlay open right now.
 *
 * Module-level because it is a fact about the page rather than about a component: two overlays that
 * know nothing of each other still have to agree on which of them Escape belongs to, and on who is
 * allowed to give the page its scrollbar back. Both of those were decided per-overlay before, and
 * both were wrong the moment a second one opened — a confirmation inside a dialog closed the dialog
 * too, and cancelling that confirmation unlocked the body while the dialog was still covering it.
 *
 * A `Symbol` per registration rather than an identifier a caller supplies: nothing outside this file
 * can name an entry, so nothing can remove somebody else's.
 */
const openOverlays: OpenOverlay[] = [];

/**
 * Which overlay an Escape belongs to: the deepest one, and among equals the one that opened last.
 *
 * **Depth rather than registration order, because registration order inverts for a genuinely nested
 * pair**. React flushes effects child-first, so a confirmation inside a
 * dialog that both become open in the SAME commit registers inner-then-outer, and a stack reading
 * its own last entry would hand Escape to the outer one — the defect this hook exists to fix,
 * surviving in the one arrangement nobody had arranged. Depth comes from the React tree through
 * {@link OverlayDepthContext}, which is decided by where a component is rendered rather than by when
 * its effect ran, so it is the same answer whichever commit the two opened in.
 *
 * **Ties go to the last registered**, which is what two independent page-level overlays want: they
 * are not nested, so the one that opened most recently is the one on top.
 *
 * DOM containment would not answer this. A modal portals into `document.body`, so a dialog and the
 * confirmation it raised are siblings in the document however deeply one is nested in the other.
 */
function topmostToken(): symbol | undefined {
  let topmost: OpenOverlay | undefined;

  for (const overlay of openOverlays) {
    if (topmost === undefined || overlay.depth >= topmost.depth) {
      topmost = overlay;
    }
  }

  return topmost?.token;
}

/**
 * The page-level behaviour every overlay shares: the body does not scroll behind it, and Escape
 * dismisses it.
 *
 * **Escape reaches the topmost overlay only** — see {@link topmostToken} for what topmost means. The
 * handler is on the document, so every open overlay hears every Escape; what decides is that
 * function. A non-dismissible overlay still takes its turn at the top and simply does nothing with
 * it — which is the point, since an Escape it refuses must not fall through to the surface
 * underneath.
 *
 * **The scroll lock is released by the last one out, not by the first.** `overflow` is set while any
 * overlay is open and cleared only when the stack empties, so an inner overlay closing cannot hand
 * the page back its scrollbar while an outer one is still covering it.
 *
 * @returns the depth an overlay rendered INSIDE this one has. An overlay component must provide it
 * to its own children through {@link OverlayDepthContext}, or a nested overlay registers as this
 * one's sibling and the two contend for Escape by mount order again.
 */
export function useOverlay({ isOpen, onClose }: UseOverlayOptions): number {
  const depth = useContext(OverlayDepthContext) + 1;

  // `onClose` is read through a ref so the effect below depends on `isOpen` alone. It is a fresh
  // function on most renders — `Modal` derives it from `dismissible` inline — and if it were a
  // dependency, an ordinary re-render of an OUTER overlay would tear its registration down and push
  // it back on top of the stack, stealing Escape from the overlay it opened.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!isOpen) return;

    const token = Symbol('overlay');
    openOverlays.push({ token, depth });
    document.body.style.overflow = 'hidden';

    const handleEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      if (topmostToken() !== token) return;

      onCloseRef.current();
    };

    document.addEventListener('keydown', handleEscape);

    return () => {
      document.removeEventListener('keydown', handleEscape);

      const index = openOverlays.findIndex((overlay) => overlay.token === token);
      if (index !== -1) openOverlays.splice(index, 1);

      if (openOverlays.length === 0) {
        document.body.style.overflow = '';
      }
    };
  }, [isOpen, depth]);

  return depth;
}
