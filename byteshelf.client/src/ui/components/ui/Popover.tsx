import { type ReactNode, type RefObject, useRef, useEffect, useLayoutEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { AnimatePresence, motion } from 'motion/react';
import { springs } from '../../lib/springs';
import { cn } from '../../lib/utils';
import { useControllableState } from '../../hooks/useControllableState';
import { anchorMoved } from '../../lib/anchorDrift';
import { anchorVisible } from '../../lib/anchorVisibility';
import {
  availableWidth,
  placePopoverPanel,
  resolvePopoverSide,
  viewportRegion,
  type PopoverAlign,
  type PopoverPosition,
} from '../../lib/popoverPlacement';
import { Squircle } from './Squircle';
import { useSquircleClipPath } from './useSquircleClipPath';
import { ROUNDED_PX } from './squircleConstants';

export type { MenuPopoverPosition, PopoverAlign, PopoverPosition } from '../../lib/popoverPlacement';

export interface PopoverProps {
  /** The element that opens the panel. Omit when positioning against an external {@link anchorRef}. */
  trigger?: ReactNode;
  /**
   * Position the panel against an external element instead of wrapping a trigger. When provided the
   * component renders no trigger of its own and is driven entirely by the controlled `open` state —
   * use this to anchor a panel to a control that already lives elsewhere (e.g. a dropdown's trigger).
   */
  anchorRef?: RefObject<HTMLElement | null>;
  /** Which edge of the anchor the panel lines up with; `auto` picks by the anchor's half of the screen. See `resolvePopoverSide`. */
  align?: PopoverAlign;
  /**
   * Which side of the anchor the panel opens on. `below` and `above` are honoured as asked, `auto`
   * picks the half of the screen the anchor is not in, and `prefer-below` — for a panel that is read,
   * not acted in — is the one that flips. See `resolvePopoverSide`.
   */
  position?: PopoverPosition;
  children: ReactNode;
  /** Classes applied to the trigger wrapper. Trigger mode only — ignored when {@link anchorRef} is
   * set, since no trigger wrapper is rendered. */
  className?: string;
  /** Controlled open state. When provided, the component is controlled. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Extra classes applied to the floating panel element. */
  panelClassName?: string;
  /**
   * Whether the panel paints its own surface (the rounded fill + hairline border + shadow). Defaults
   * to `true`. Set `false` when the caller renders its own surfaces inside the panel (e.g. several
   * distinct cards side by side) so the panel itself stays transparent.
   */
  renderSurface?: boolean;
  /**
   * Whether the PANEL scales in, which is the entrance every popover has by default.
   *
   * <b>Set `false` when something inside the panel positions itself, and give that thing its own
   * entrance.</b> The scale is a `transform` on the panel, and a transformed element is the
   * containing block for a `position: fixed` descendant — so for the length of the animation such a
   * descendant's `left`/`top` are resolved in the panel's coordinates and scaled with it, and it is
   * exactly right only on the frame the scale reaches 1. A spring arrives at 1 asymptotically, so
   * "exactly right at the end" is a few pixels of drift that resolve as a shift just as the movement
   * should be stopping. Compensating for it is arithmetic against a value that changes every frame;
   * not transforming the panel removes the coupling instead.
   *
   * The entrance is not lost, it moves: the card inside carries `animate-scale-in` on itself, as
   * `ordo.client`'s `EditorPopoverCard` did until #2941, where a transform affects only how the card
   * paints and not the coordinates it is positioned by. Exit still fades, which needs no transform.
   */
  animatePanel?: boolean;
  /** Extra classes applied to the trigger wrapper. */
  triggerClassName?: string;
  /**
   * Whether clicking the trigger toggles open state. Defaults to `true`. Set `false` for
   * typeahead consumers (e.g. `Combobox`) that drive open state from their input's focus/typing
   * so a click on the already-open input doesn't close the panel.
   */
  toggleOnTriggerClick?: boolean;
  /**
   * A region of the page the panel is kept inside instead of the whole window: placed, sided and
   * clamped against the part of this element that is on screen (`viewportRegion`). Omitted, the
   * panel is kept inside the window as before.
   *
   * For floating chrome that belongs to one pane of a split page and must not spill over the pane
   * beside it — the report editor keeps its History and Comments cards in the document's pane, off
   * the source opened to its right (#2807). Re-placed when the region resizes, since a pane resizes
   * when a neighbour opens without the window or the anchor changing.
   */
  boundaryRef?: RefObject<HTMLElement | null>;
  /**
   * Called once per opening, as soon as the panel is placed and shown — the first moment an element in it
   * can take focus. Optional, and nothing else changes with it.
   *
   * <b>For a caller that moves focus into the panel</b>, such as a popover opened from a menu or the
   * keyboard. Until it is placed the panel is `visibility: hidden`, which refuses focus, so focusing from
   * the caller's own effect would race the placement. Called from the same layout pass that places it,
   * before the browser paints, with the reveal's visibility transitions finished (`finishRevealTransitions`).
   */
  onShown?: () => void;
  /**
   * Whether a click outside the panel and its anchor closes it. Defaults to `true`. Set `false` for a panel
   * read beside the controls around it, which must stay open while those are used (the QC run overview's
   * reasoning popover, #3000): Escape, a scroll that moves the anchor, and the anchor going out of view
   * still close it.
   */
  closeOnOutsideClick?: boolean;
}

/**
 * Ends the visibility transitions the panel's reveal started, so what it holds can take focus at once.
 *
 * <b>The reveal is a `visibility` flip, and a descendant whose `transition-property` covers it animates
 * it</b> — any `transition-all`, which `Button` carries. Measured in Chromium (#2795): such a button stays
 * computed `hidden` until the transition's first frame, a hidden element refuses `focus()`, and so a
 * caller focusing from {@link PopoverProps.onShown} saw nothing happen. A visibility transition draws
 * nothing in between, so finishing it changes nothing anyone sees.
 *
 * On the `onShown` path only, which is the one that promises focus can be taken. A test DOM without the
 * Web Animations API runs no transitions, so has none to finish.
 */
function finishRevealTransitions(panel: HTMLElement) {
  if (typeof panel.getAnimations !== 'function') return;
  // `getAnimations` brings style up to date first, so the transition the flip starts is among these.
  for (const animation of panel.getAnimations({ subtree: true })) {
    if ('transitionProperty' in animation && animation.transitionProperty === 'visibility') animation.finish();
  }
}

/** The room the panel's side has, published to the panel's own content so a caller can fit itself. */
export const POPOVER_AVAILABLE_HEIGHT_VAR = '--popover-available-height';

/**
 * The WIDTH the panel's region leaves it, in whole pixels, published beside
 * {@link POPOVER_AVAILABLE_HEIGHT_VAR} for a caller that sizes itself: the window less its margins,
 * or a `boundaryRef` region's width less them. A panel with a width of its own caps itself at
 * `max-w-[var(--popover-available-width,100vw)]` and wraps, rather than overhanging its region —
 * the report editor's History card in a document pane narrower than the card (#2807). Published, never
 * applied, since most panels are content-sized and narrower than any room they are given.
 */
export const POPOVER_AVAILABLE_WIDTH_VAR = '--popover-available-width';

/**
 * Caps a panel's content at the room its side has, and lets it scroll — but only when that cap is
 * both needed and the tighter of the two in play.
 *
 * <b>It also PUBLISHES that room, always, as {@link POPOVER_AVAILABLE_HEIGHT_VAR}.</b> The cap below
 * is a backstop that scrolls the whole panel, and a panel with its own scrolling region then has two:
 * an outer one that slides the entire card — heading, surface and all — past the edges of a box it
 * has no way to know about, which reads as the card being sliced off at the top or the bottom. A
 * caller that reads this variable caps ITSELF at the room instead (`max-h-[var(--popover-available-height,…)]`),
 * so its own region is the only thing that scrolls and this function's `scrollHeight` check then
 * finds the content fitting and writes nothing at all. `EditorPopoverCard` in `ordo.client` was the
 * worked example until #2941 removed it. Set unconditionally and before the measurements below, both for the same reason:
 * it is what the content is measured WITH, so a variable written after the check, or only on the
 * panels that overrun, would be describing a layout that no longer holds. It overrides nothing —
 * a custom property nobody reads has no effect — which is what makes "always" safe here and not two
 * lines up.
 *
 * Three properties of the CAP, each of which was a defect in the first version of this:
 *
 * - **It writes nothing when the content already fits.** An inline `max-height` and an inline
 *   `overflow-y` are written to the same element the caller's `panelClassName` lands on, and inline
 *   always beats a class. Writing unconditionally therefore reached into every panel in the product,
 *   not only the ones that overrun.
 * - **A caller's own cap wins when it is tighter.** `max-h-96` on an admin log panel is a design
 *   decision about that panel; this is a backstop against a panel leaving the screen. Where the two
 *   disagree, the smaller one is the one that was chosen deliberately.
 * - **`overflow-y` goes on only with the cap.** Per CSS Overflow 3 §3, an axis computing to `visible`
 *   beside one computing to `auto` is forced to `auto` too — so setting `overflow-y` alone silently
 *   makes the panel clip horizontally, which is exactly what `shouldClip` above exists to keep
 *   *opt-in*: an uncapped panel is left free to let a focus ring or a nested overlay escape its box.
 *
 * Clearing before measuring is what makes it reversible: a panel that gains room back — a window
 * resized taller, an anchor that moved down the page — is released rather than left capped at
 * whatever the tightest moment of its life was.
 */
/** Takes off the cap {@link applyHeightCap} writes — everything it writes but the published room. */
function clearHeightCap(content: HTMLDivElement): void {
  content.style.maxHeight = '';
  content.style.overflowY = '';
}

function applyHeightCap(content: HTMLDivElement | null, maxHeight: number): void {
  if (!content) return;

  // WHOLE PIXELS, and this is not tidiness. `maxHeight` is derived from a `DOMRect`, so it is
  // routinely fractional — 484.4 — while `scrollHeight` is an integer, rounded UP from the layout.
  // A panel that fits itself to a fractional cap therefore measures back as 485 against a limit of
  // 484.4, and the backstop below engages on a panel that did exactly what it was told. That is not
  // a cosmetic overshoot: the cap comes with `overflow-y`, which per the note below forces
  // `overflow-x` to match, and the panel becomes a clipping box the size of itself — cropping a
  // squircle's drop-shadow and, on a card that can be dragged, cropping the card as it moves. It
  // reproduced as an INTERMITTENT clip, because it needs the panel to be tall enough to reach its cap
  // and the anchor to land on a fraction.
  const room = Math.floor(maxHeight);

  clearHeightCap(content);
  content.style.setProperty(POPOVER_AVAILABLE_HEIGHT_VAR, `${room}px`);

  // `NaN` for the `none` computed by a panel whose classes impose no cap of their own, which is most
  // of them — and every comparison against `NaN` is false, so that case falls through to ours.
  const authoredCap = Number.parseFloat(window.getComputedStyle(content).maxHeight);
  if (authoredCap <= room) return;

  // LAYOUT height, not scroll height — the cap having been cleared two lines up, this is the
  // content's natural height either way, and the two differ on exactly the thing that must not count.
  //
  // `scrollHeight` is the extent of the content's SCROLL AREA, which includes a descendant that has
  // been transformed out of the box. A transform changes paint, not layout, so a panel holding a card
  // the reader has DRAGGED was answering "would I overrun the room?" with the card's displacement:
  // drag it down or right, grow it to the room, and the sum exceeds the room by however far it was
  // moved. The cap then engaged — and it comes with `overflow-y`, which per the note above forces
  // `overflow-x`, so the panel became a clipping box AT THE ANCHORED POSITION while the card was
  // being painted at the dragged one. It read as the card flying off-screen, leaving a scrollbar
  // where it would have been had it never been moved (#2220). Measured, not reasoned: a 592px room
  // with the card dragged 160px down measured back 752.
  //
  // It reproduced only for a card BOTH dragged away from its anchor and tall enough to reach the
  // room, which is why it looked like it depended on which paragraph the history was opened for.
  //
  // `offsetHeight` ignores transforms by definition — the same property a draggable card in a panel
  // has to measure itself with to be placed at all. Overflow that is not in flow — an
  // absolutely positioned decoration, a squircle's shadow layer — stops counting too, which is also
  // correct: neither is content a reader would scroll to.
  if (content.offsetHeight <= room) return;

  content.style.maxHeight = `${room}px`;
  content.style.overflowY = 'auto';
}

/** The panel's height with the cap {@link applyHeightCap} may have left on its content cleared. */
function measureUncapped(panel: HTMLElement, content: HTMLDivElement | null): number {
  if (content) clearHeightCap(content);
  return panel.offsetHeight;
}

export function Popover({
  trigger,
  anchorRef,
  align = 'auto',
  position = 'auto',
  children,
  className = '',
  open: controlledOpen,
  onOpenChange,
  panelClassName = '',
  renderSurface = true,
  animatePanel = true,
  triggerClassName = '',
  toggleOnTriggerClick = true,
  onShown,
  boundaryRef,
  closeOnOutsideClick = true,
}: PopoverProps) {
  const [open, setOpen] = useControllableState(controlledOpen, onOpenChange, false);

  const triggerRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  // The anchor rect the panel is currently painted against. `handleScroll` compares the anchor's
  // live rect with it to tell a scroll that moved the anchor from one that did not — see there.
  const positionedAgainstRef = useRef<DOMRect | null>(null);
  // Set when this component has asked to close because its anchor moved — a scroll's close or the
  // anchor watch's — and React has not yet taken the panel away. The anchor watch reads it; see there.
  const closingForAnchorRef = useRef(false);
  // The element the panel positions against: an external anchor when given, otherwise our own
  // trigger wrapper.
  const getAnchorEl = useCallback(
    (): HTMLElement | null => anchorRef?.current ?? triggerRef.current,
    [anchorRef],
  );
  // Clip the content to the squircle ONLY when the caller opts in via `overflow-hidden` — the same
  // signal that drove clipping before squircles (menus do; free-content panels don't). It keeps a
  // scroller or a footer inside the rounded shape, but would otherwise crop content
  // meant to escape (focus-ring offsets, nested overlays). Non-clipping panels still get the rounded
  // shape + border from the Squircle layer below; only their content is left un-clipped.
  const shouldClip = /\boverflow-hidden\b/.test(panelClassName);
  const { ref: clipRef, clipPath } = useSquircleClipPath<HTMLDivElement>(ROUNDED_PX.xl);

  // The content layer, which is what carries the height cap and the scroll — not the wrapper. The
  // wrapper is the containing block for the `Squircle` painting the surface, and an absolutely
  // positioned child scrolls with its containing block's content, so capping the wrapper instead
  // would scroll the border and shadow away from the box they are drawing.
  const contentRef = useRef<HTMLDivElement | null>(null);
  const setContentRef = useCallback(
    (node: HTMLDivElement | null) => {
      contentRef.current = node;
      // `clipRef` measures for the clip path and is only wanted when the caller opted into clipping;
      // it is a stable callback, so composing it here costs nothing per render.
      if (shouldClip) clipRef(node);
    },
    [shouldClip, clipRef],
  );

  const close = useCallback(() => setOpen(false), [setOpen]);

  // Position the panel against its anchor. Re-runnable so it can be recomputed whenever the panel's
  // own size changes — see the ResizeObserver below.
  const positionPanel = useCallback(() => {
    const anchorEl = getAnchorEl();
    const panel = panelRef.current;
    if (!panel || !anchorEl) return;
    const rect = anchorEl.getBoundingClientRect();
    // Re-baselines the dismissal rule, and deliberately: this runs again on every panel or anchor
    // RESIZE and on every anchor MOVE without a scroll (see the effects below), so `handleScroll`
    // compares against where the panel was painted last, not where it first opened. The two only
    // differ when the anchor moved without a scroll, and after this the panel is painted against the
    // anchor's new position, which is what makes the later comparison mean what it says.
    positionedAgainstRef.current = rect;

    const viewport = viewportRegion(boundaryRef?.current);
    // Published before the panel is measured, so a caller that caps its width at it is measured at the
    // width it will be drawn at, and the side and clamp below are worked out for that box.
    contentRef.current?.style.setProperty(POPOVER_AVAILABLE_WIDTH_VAR, `${availableWidth(viewport)}px`);
    // Which side, and how much room it has — before the panel is measured, because capping its height
    // is what changes the measurement. `prefer-below` is the exception that needs a height first: it
    // flips only for a panel that will not fit below, so it reads the panel's NATURAL height, with
    // the cap a previous placement left cleared. Read capped, a panel squeezed below once would
    // measure as fitting there and never flip back.
    const naturalHeight = position === 'prefer-below' ? measureUncapped(panel, contentRef.current) : undefined;
    const side = resolvePopoverSide(rect, viewport, align, position, naturalHeight);
    applyHeightCap(contentRef.current, side.maxHeight);

    // Measured after the cap, so this is the height the panel actually took.
    const placement = placePopoverPanel(rect, viewport, side, {
      width: panel.offsetWidth,
      height: panel.offsetHeight,
    });

    panel.style.left = `${placement.left}px`;
    panel.style.top = `${placement.top}px`;
    panel.style.transformOrigin = placement.transformOrigin;
    panel.style.visibility = 'visible';
  }, [align, position, getAnchorEl, boundaryRef]);

  // Position on open, then keep the panel anchored as either box's size changes while open.
  //
  // The PANEL, because async content can load in after the first measurement and would otherwise
  // leave an above-placed panel anchored too low until the next open.
  //
  // The ANCHOR, because a growing anchor moves an above-placed panel's target just as surely: a
  // composer whose textarea auto-resizes pushes its own top edge upward, and a panel positioned once
  // would be left overlapping the control it belongs to. Observing it re-baselines
  // `positionedAgainstRef` exactly as a panel resize does, which is the behaviour that comment
  // already describes and relies on — the panel is repainted against the anchor's new rect, so the
  // scroll-dismissal comparison (#1837) goes on meaning what it says.
  // The latest `onShown`, so a caller passing a new function each render does not re-run the placement.
  const onShownRef = useRef(onShown);
  useLayoutEffect(() => {
    onShownRef.current = onShown;
  });
  // Whether this opening has been announced through `onShown` — once per opening, not once per placement.
  const announcedRef = useRef(false);

  useLayoutEffect(() => {
    if (!open || !panelRef.current) return;
    positionPanel();
    if (!announcedRef.current) {
      announcedRef.current = true;
      if (onShownRef.current) {
        finishRevealTransitions(panelRef.current);
        onShownRef.current();
      }
    }

    const observer = new ResizeObserver(() => positionPanel());
    observer.observe(panelRef.current);
    const anchorEl = getAnchorEl();
    if (anchorEl) observer.observe(anchorEl);
    // The REGION, because a pane narrows when a neighbour opens without the anchor or the window
    // changing — and the panel's room is the region's.
    if (boundaryRef?.current) observer.observe(boundaryRef.current);
    return () => {
      observer.disconnect();
      // Drop the measurement with the panel it described, so a later open can never compare a live
      // anchor against where a previous one used to be.
      positionedAgainstRef.current = null;
    };
  }, [open, positionPanel, getAnchorEl, boundaryRef]);

  // A closed panel's next opening is announced again.
  useLayoutEffect(() => {
    if (!open) announcedRef.current = false;
  }, [open]);

  // Follow the anchor's POSITION, which neither observation above sees (#2515): content loading in
  // above the anchor moves it down the page without resizing it, and without a scroll for
  // `handleScroll` to hear, leaving the panel where it opened and pointing at whatever slid in under
  // it. A moved anchor that can still be seen is followed, exactly as a resized one is; the reader
  // did not cause the move, so closing on it would take the panel away for nothing they did. One that
  // can no longer be seen closes the panel instead — below the fold, or past the edge of the list it
  // scrolls in — since following it would leave the panel pinned to an anchor nobody can see, the
  // same thing #1837 closes on a scroll. `anchorVisible` owns what "seen" means.
  //
  // Checked once a frame, for as long as the panel is open, against the rect it was last painted
  // against. Nothing cheaper sees every cause: a move is a consequence of layout ANYWHERE above the
  // anchor — a fetch landing, an image decoding, a font swapping, a sibling animating its height —
  // and no event names it. The cost is one `getBoundingClientRect` a frame, which reads the layout
  // the frame computes anyway; the walk up the ancestors `anchorVisible` makes happens only on a
  // frame where the anchor did move.
  //
  // <b>A scroll is still `handleScroll`'s to decide, and the two can meet in one frame.</b> A scroll
  // event is dispatched before this frame's callbacks, but React renders the close it asks for at a
  // non-urgent priority, in a later task — so the panel that scroll is closing is still open when this
  // looks, and its anchor has moved. Acting on it would either move a closing panel to the scrolled
  // anchor, so it fades out from the wrong place, or close it a second time, and `onOpenChange(false)`
  // reaches the caller twice. Hence `closingForAnchorRef`: whichever of the two asks first, this
  // stops watching. A caller that refuses the close keeps the panel with the scroll rule still on it
  // and without this watch until the effect next runs.
  useEffect(() => {
    if (!open) return;
    closingForAnchorRef.current = false;

    let frame = requestAnimationFrame(function watchAnchor() {
      if (closingForAnchorRef.current) return;
      const anchor = getAnchorEl();
      const positionedAgainst = positionedAgainstRef.current;
      const rect = anchor?.getBoundingClientRect();
      if (anchor && rect && positionedAgainst && anchorMoved(rect, positionedAgainst)) {
        if (anchorVisible(anchor, rect)) {
          positionPanel();
        } else {
          closingForAnchorRef.current = true;
          close();
        }
      }
      frame = requestAnimationFrame(watchAnchor);
    });
    return () => cancelAnimationFrame(frame);
  }, [open, close, getAnchorEl, positionPanel]);

  useEffect(() => {
    if (!open) return;

    function handleClickOutside(event: MouseEvent) {
      const target = event.target as Node;
      const anchorEl = getAnchorEl();
      if (
        anchorEl && !anchorEl.contains(target) &&
        !(event.target as HTMLElement).closest('[data-popover-panel]')
      ) {
        close();
      }
    }

    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        close();
      }
    }

    // Dismiss on a scroll that moved the anchor, and only then (#1837). Why anchor movement rather
    // than a `scroll` event is the condition at all is argued once, at `anchorMoved`; what is local
    // to this component is below.
    //
    // Closing is what stops the panel detaching from a trigger inside `FileList` or
    // `ChatSessionList`. It subsumes the target filter it replaces (ignore scrolls originating
    // inside the panel): a panel-internal scroll cannot move an anchor that lives outside the panel.
    // Where the two differ — an anchor that itself sits inside another popover's panel — closing is
    // the right answer, and the old filter kept a detached panel open.
    //
    // Following the anchor instead of closing is the alternative `anchorMoved` leaves to a caller.
    // This component follows an anchor that moved WITHOUT a scroll (#2515, the anchor watch above),
    // but a scroll stays a close: the reader made that move, and a panel that slid along with every
    // scroll would be repositioned on every frame of it. The scroll's close is marked in
    // `closingForAnchorRef`, so the watch does not act on the same move before React renders it.
    function dismissForAnchor() {
      closingForAnchorRef.current = true;
      close();
    }

    function handleScroll() {
      const anchorEl = getAnchorEl();
      const positionedAgainst = positionedAgainstRef.current;
      // Nothing to compare against: fall back to the unconditional close this replaces, rather than
      // leave a panel open on a measurement we do not have.
      //
      // Reached by an anchor that GOES AWAY under an open panel, not by one that never arrived — a
      // `ref` bound in JSX reads `null` once its element unmounts, so a list row removed by a
      // refetch under its own open menu lands here. Do not re-derive this population from the
      // `anchorRef` call sites: every one of those binds the ref to an element the user had to click
      // to open the panel, so the never-mounted case cannot occur and a reader checking for it would
      // conclude the guard is dead.
      if (!anchorEl || !positionedAgainst) {
        dismissForAnchor();
        return;
      }
      if (anchorMoved(anchorEl.getBoundingClientRect(), positionedAgainst)) {
        dismissForAnchor();
      }
    }

    if (closeOnOutsideClick) document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleEscape);
    window.addEventListener('scroll', handleScroll, true);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleEscape);
      window.removeEventListener('scroll', handleScroll, true);
    };
  }, [open, close, getAnchorEl, closeOnOutsideClick]);

  const panel = createPortal(
    <AnimatePresence>
      {open && (
        <motion.div
          ref={panelRef}
          // Load-bearing: the handlers above resolve the panel through it, and tests outside this
          // package find an open panel by it — including ordo.client's e2e capture helper. Keep it
          // on this element, not the content div; see the Squircle comment below.
          data-popover-panel
          // No transform at all when the panel does not animate — not `scale: 1`, which motion still
          // writes as a transform and which is still a containing block. See `animatePanel`.
          initial={animatePanel ? { opacity: 0, scale: 0.4 } : false}
          animate={animatePanel ? { opacity: 1, scale: 1 } : undefined}
          exit={animatePanel
            ? { opacity: 0, scale: 0.4, transition: { duration: 0.15 } }
            : { opacity: 0, transition: { duration: 0.12 } }}
          transition={springs.snappy}
          // top/left/transformOrigin are placeholders — the positioning effect
          // overwrites them (and flips visibility to visible) before paint.
          style={{
            position: 'fixed',
            top: -9999,
            left: -9999,
            transformOrigin: 'left top',
            visibility: 'hidden',
          }}
          className="z-50 isolate"
        >
          {/* Squircle paints the rounded fill + 1px border + shadow behind the content (so every
              panel keeps its hairline border, which a clip-path can't stroke). The content layer is
              transparent; it's clipped to the same squircle only when the caller opted into clipping
              (`shouldClip`). The shadow lives on this SVG, not the panel box, so it follows the curve.
              Skipped entirely when `renderSurface` is false — the caller paints its own surfaces.
              This is also why `data-popover-panel` sits on the wrapper above rather than on the
              content div: wherever the panel paints its own surface, only the wrapper encloses both
              this SVG and the content, so a screenshot scoped any deeper loses border, fill and
              shadow. */}
          {renderSurface && (
            <Squircle
              cornerRadius={ROUNDED_PX.xl}
              fillClassName="fill-panel"
              borderClassName="stroke-border"
              shadowClassName="drop-shadow-brand-lg"
            />
          )}
          <div
            ref={setContentRef}
            className={panelClassName}
            style={shouldClip ? { clipPath } : undefined}
          >
            {children}
          </div>
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );

  // Anchored mode: no trigger of our own — the parent owns the controlled open state and points us
  // at an external element to position against.
  if (anchorRef) {
    return panel;
  }

  return (
    <div ref={triggerRef} className={`inline-flex ${className}`}>
      <div
        onClick={toggleOnTriggerClick ? () => setOpen(!open) : undefined}
        className={cn(toggleOnTriggerClick && 'cursor-pointer', triggerClassName)}
      >
        {trigger}
      </div>
      {panel}
    </div>
  );
}
