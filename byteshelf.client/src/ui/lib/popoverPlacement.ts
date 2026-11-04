/**
 * Where a floating panel goes, and how tall it may be — the arithmetic behind a `position: fixed`
 * overlay anchored to an element.
 *
 * Extracted from `Popover` for the reason `anchorDrift` was: this is geometry rather than component
 * behaviour, and it is testable as plain function calls instead of through a DOM stubbed enough to
 * lay something out. Of what is here, {@link clampToViewport} and its point type are what the package
 * exports, on the rule `anchorDrift` set: a consumer outside the package wants the answer, not the
 * constants the answer is computed from. Anchor-relative placement stays internal because `Popover`
 * is the only thing that anchors a panel; keeping a fixed box on the screen is wanted by anything a
 * reader can move around the window themselves, which is what earned that one its way out.
 *
 * Split in two because the two halves are answered at different moments. Which side a panel goes on,
 * and how much room that side has, depend only on the anchor and the viewport — and have to be known
 * *before* the panel is measured, since capping its height changes it. Where the panel's corner
 * lands then depends on the size it actually took. A single function would have to be called with a
 * height that its own return value invalidates.
 */

export type PopoverAlign = 'left' | 'right' | 'auto';
export type PopoverPosition = 'below' | 'above' | 'auto' | 'prefer-below';
/**
 * The sides a menu may ask for: every one but `prefer-below`, which flips, and a flip would move a
 * menu out from under the cursor that opened it. `Dropdown`, `QuickPrompt` and `ConfirmDelete` take
 * this, so the compiler holds that rule rather than their docs.
 */
export type MenuPopoverPosition = Exclude<PopoverPosition, 'prefer-below'>;

/** Gap between an above-placed panel and its anchor. */
export const ABOVE_GAP_PX = 8;
/** Gap between a below-placed panel and its anchor. */
export const BELOW_GAP_PX = 6;
/** Smallest gap kept between a panel and the edge of the viewport. */
export const VIEWPORT_MARGIN_PX = 8;
/**
 * Floor under the height a panel is clamped to, for an anchor with almost no room on the side it was
 * placed on.
 *
 * A panel overlapping its own anchor is bad; one clamped to nothing is unusable, and its content is
 * then unreachable by any means — which is the failure this clamping exists to remove, so it must not
 * be reintroduced at the small end. Where this floor wins, the panel is knowingly allowed to overrun
 * the edge it was clamped against.
 */
export const MIN_PANEL_HEIGHT_PX = 120;

/** The part of an anchor's rect that decides placement. */
export interface AnchorBox {
  top: number;
  bottom: number;
  left: number;
  right: number;
  width: number;
}

/**
 * The box a fixed overlay is kept inside: the window, unless a caller names a smaller region of it.
 *
 * <b>`left` and `top` are always stated, and the window's are 0.</b> A region is for floating chrome
 * that belongs to one pane of a page and must not drift over its neighbours — the report editor's
 * History card stays in the document's pane rather than covering the source opened beside it (#2807).
 * {@link viewportRegion} is how a caller gets one, for a region or for the window alike.
 */
export interface Viewport {
  left: number;
  top: number;
  width: number;
  height: number;
}

/**
 * The window, or the part of `region` that is inside it: the {@link Viewport} a fixed overlay kept to
 * one pane of a page is clamped against. Read at the moment it is used, because a pane resizes with
 * its neighbours and the window with the browser.
 *
 * <b>Intersected with the window rather than taken as the element's rect</b>, so a pane that runs
 * past an edge of the screen never lets an overlay follow it there: off-screen is unreachable however
 * the box is described. A region with no area left on screen falls back to the window, which keeps an
 * overlay reachable rather than pinning it into a box nobody can see.
 */
export function viewportRegion(region: Element | null | undefined): Viewport {
  const wholeWindow = { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
  if (!region) return wholeWindow;
  const rect = region.getBoundingClientRect();
  const left = Math.max(rect.left, 0);
  const top = Math.max(rect.top, 0);
  const right = Math.min(rect.right, window.innerWidth);
  const bottom = Math.min(rect.bottom, window.innerHeight);
  if (right <= left || bottom <= top) return wholeWindow;
  return { left, top, width: right - left, height: bottom - top };
}

/**
 * The widest a panel may be while keeping {@link VIEWPORT_MARGIN_PX} clear of both sides of
 * `viewport`, in whole pixels, and never below 0.
 *
 * The horizontal counterpart of {@link PopoverSide.maxHeight}, and answered on its own rather than by
 * {@link resolvePopoverSide} because it is needed EARLIER: `Popover` publishes it for a panel that
 * caps its own width, and the panel has to be measured at that width before its side can be chosen.
 * Floored, for the reason the height cap is: the room is derived from a fractional rect, and a panel
 * sized to a fraction measures back a pixel over it.
 */
export function availableWidth(viewport: Viewport): number {
  return Math.max(Math.floor(viewport.width - 2 * VIEWPORT_MARGIN_PX), 0);
}

/** The side a panel was placed on, and the room it has there. */
export interface PopoverSide {
  align: 'left' | 'right';
  position: 'above' | 'below';
  /**
   * The tallest the panel may be on this side while staying inside the viewport, floored at
   * {@link MIN_PANEL_HEIGHT_PX}.
   */
  maxHeight: number;
}

/**
 * Resolves `auto` align/position against the anchor's position in the viewport, and measures the room
 * left on the side chosen.
 *
 * `auto` picks the half of the screen the anchor is NOT in, which is what leaves a panel room to open
 * without covering the thing it belongs to. An explicit side is honoured as given: a caller asking for
 * `above` gets `above`, clamped, rather than silently flipped — the flip would move a menu out from
 * under the cursor that opened it. Menus (`Dropdown`, `Select`, `QuickPrompt`) keep that rule.
 *
 * `prefer-below` is the one side that flips, and it is for a panel that is READ rather than acted in —
 * `InfoPopover`'s explanation. It opens below, and goes above only when `panelHeight` (its natural
 * height, measured with no cap of ours on it) does not fit below and does fit above. Held below
 * regardless, an ⓘ near the bottom edge has its explanation clamped up over the ⓘ itself, so the
 * reader cannot see what is being explained; there is no cursor to keep the panel under, because
 * nothing in it is pressed. Where it fits NEITHER side it takes the side with more room, capped
 * there: held below, an ⓘ with almost nothing beneath it would have its explanation floored at
 * {@link MIN_PANEL_HEIGHT_PX} and clamped back over it — the same overlap, reached through a taller
 * explanation or a shorter window. A tie stays below. `panelHeight` is read only for
 * `prefer-below`, and without it that side cannot flip.
 */
export function resolvePopoverSide(
  anchor: AnchorBox,
  viewport: Viewport,
  align: PopoverAlign,
  position: PopoverPosition,
  panelHeight?: number,
): PopoverSide {
  const anchorCenterX = anchor.left + anchor.width / 2;
  const anchorCenterY = anchor.top + (anchor.bottom - anchor.top) / 2;

  const resolvedAlign: 'left' | 'right' =
    align === 'auto' ? (anchorCenterX >= viewport.left + viewport.width / 2 ? 'right' : 'left') : align;
  const availableAbove = anchor.top - viewport.top - ABOVE_GAP_PX - VIEWPORT_MARGIN_PX;
  const availableBelow = viewport.top + viewport.height - anchor.bottom - BELOW_GAP_PX - VIEWPORT_MARGIN_PX;

  let resolvedPosition: 'above' | 'below';
  if (position === 'auto') {
    resolvedPosition = anchorCenterY >= viewport.top + viewport.height / 2 ? 'above' : 'below';
  } else if (position === 'prefer-below') {
    resolvedPosition = preferBelow(panelHeight, availableBelow, availableAbove);
  } else {
    resolvedPosition = position;
  }

  return {
    align: resolvedAlign,
    position: resolvedPosition,
    maxHeight: Math.max(
      resolvedPosition === 'above' ? availableAbove : availableBelow,
      MIN_PANEL_HEIGHT_PX,
    ),
  };
}

/** `prefer-below`'s side — see {@link resolvePopoverSide}. */
function preferBelow(panelHeight: number | undefined, availableBelow: number, availableAbove: number): 'above' | 'below' {
  if (panelHeight === undefined || panelHeight <= availableBelow) return 'below';
  if (panelHeight <= availableAbove) return 'above';
  return availableAbove > availableBelow ? 'above' : 'below';
}

export interface PanelSize {
  width: number;
  height: number;
}

export interface PopoverPlacement {
  left: number;
  top: number;
  transformOrigin: string;
}

/** A fixed box's top-left corner, in viewport coordinates. */
export interface ViewportPoint {
  left: number;
  top: number;
}

/**
 * The nearest position to `desired` that keeps the whole of a fixed box inside the viewport.
 *
 * <b>One home for "pull a fixed box back inside every edge".</b> Nothing `position: fixed` that ends
 * up outside the viewport is within reach: no scroll brings it back and no click lands on it, so an
 * overrun is not awkward, it is gone. Every overlay this package places wants that arithmetic, and so
 * does anything a reader can move around the window themselves — a second implementation beside this
 * one would agree with it only by coincidence, including on the margin, which is why the margin is
 * baked in here rather than passed.
 *
 * <b>A box LARGER than the room clamps to the NEAR edge rather than to nothing.</b> A box taller than
 * the window satisfies neither limit, so one of them has to win, and it must be the top: a panel's
 * first line is there, and a card's heading and close button with it. Which one wins is decided by
 * nothing more than the nesting of the two calls below, so it is asserted rather than left to be read
 * off — the first version of the card's own copy of this had it the other way round.
 */
export function clampToViewport(
  desired: ViewportPoint,
  box: PanelSize,
  viewport: Viewport,
): ViewportPoint {
  const minLeft = viewport.left + VIEWPORT_MARGIN_PX;
  const minTop = viewport.top + VIEWPORT_MARGIN_PX;
  const maxLeft = Math.max(minLeft, minLeft + viewport.width - box.width - 2 * VIEWPORT_MARGIN_PX);
  const maxTop = Math.max(minTop, minTop + viewport.height - box.height - 2 * VIEWPORT_MARGIN_PX);

  return {
    left: Math.min(Math.max(desired.left, minLeft), maxLeft),
    top: Math.min(Math.max(desired.top, minTop), maxTop),
  };
}

/**
 * Places a measured panel against its anchor, pulled back inside the viewport on every edge.
 *
 * The clamping is the point, and it is {@link clampToViewport} that does it — including the case of a
 * panel larger than the viewport, which pins to the near edge rather than being pushed off the
 * opposite one by a bound that has gone negative.
 *
 * That the clamp runs on BOTH axes is not incidental to the height cap above. Capping the height
 * (see {@link resolvePopoverSide}) keeps a panel inside the edge it opens towards in every case but one — where
 * {@link MIN_PANEL_HEIGHT_PX} won — and in that one case an above-placed panel overruns the top while
 * a below-placed one overruns the bottom. Clamping only the edge that happens to be nearer the code
 * you are reading is how the second of those survives the first being fixed.
 */
export function placePopoverPanel(
  anchor: AnchorBox,
  viewport: Viewport,
  side: PopoverSide,
  panel: PanelSize,
): PopoverPlacement {
  const unclampedLeft = side.align === 'right' ? anchor.right - panel.width : anchor.left;
  const unclampedTop =
    side.position === 'above'
      ? anchor.top - panel.height - ABOVE_GAP_PX
      : anchor.bottom + BELOW_GAP_PX;

  return {
    ...clampToViewport({ left: unclampedLeft, top: unclampedTop }, panel, viewport),
    transformOrigin: `${side.align === 'right' ? 'right' : 'left'} ${side.position === 'above' ? 'bottom' : 'top'}`,
  };
}
