import { cn } from '../../lib/utils';

/**
 * The floor a menu panel is drawn at, as a number, for the one reader that needs it as one: a
 * browser spec bounding a menu's width (#2377). A panel sitting exactly on this is the strongest
 * available statement that nothing inside it is setting the width — much stronger than a fraction
 * of the sentence it no longer carries, which would bless a menu half the width of a paragraph.
 *
 * <b>It cannot be interpolated into the class below, and that is Tailwind's rule rather than a
 * choice.</b> An arbitrary value is generated only if the literal `min-w-[180px]` appears in scanned
 * source, so a template string would compile, read correctly here, and leave the panel with no
 * minimum at all — visible as nothing until a one-item menu drew as a sliver. The two are held
 * together by `menuPanelStyles.test.ts` instead, which is a test and not a comment for exactly that
 * reason.
 */
export const MENU_PANEL_MIN_WIDTH_PX = 180;

/**
 * How far every menu row's pill sits in from the panel's edge, on each side, as a number — for the
 * one reader that has to subtract it: `Combobox`, whose list is sized to its trigger in pixels and
 * would otherwise draw a panel this much wider than the field on each side.
 *
 * Written out in {@link MENU_PANEL_INSET_CLASS} as `p-1` for the reason {@link MENU_PANEL_MIN_WIDTH_PX}
 * is written out in its class, and held to it by `menuPanelStyles.test.ts` the same way.
 */
export const MENU_PANEL_INSET_PX = 4;

/**
 * The menu panel's inset alone, on every side: the margin each row's pill sits in. Part of
 * {@link MENU_PANEL_BOX_CLASS}; on its own only for a menu drawn in a `Card` rather than a `Popover`
 * surface (`FilterBar`'s field list), where the clip that box carries would cut off the card's shadow.
 */
export const MENU_PANEL_INSET_CLASS = 'p-1';

/**
 * The box of a menu-style `Popover` panel without a width: an inset on EVERY side, and clipped
 * corners. The inset is the margin each row's pill sits in — the panel's edge, then a gap, then the
 * pill — on all four sides, so the first and last rows are pills exactly like the middle ones (#2455).
 *
 * For a panel that states its own width (the assistant's attach menu is `w-64`, the pickers built on
 * `ListButton` are `w-72` / `w-80`) — pass the width beside it. Everything else takes
 * {@link MENU_PANEL_CLASS}, which is this plus the floor width.
 *
 * <b>`overflow-hidden` is not a layout hint.</b> `Popover` reads it as the opt-in that clips its
 * content to the squircle it paints, so it stays even though the inset rows never reach the corners:
 * it is what keeps anything else in the panel — a scroller, a footer — inside the silhouette.
 */
export const MENU_PANEL_BOX_CLASS = `overflow-hidden ${MENU_PANEL_INSET_CLASS}`;

/** Shared `panelClassName` for the floating panel of a menu-style `Popover`: {@link MENU_PANEL_BOX_CLASS}
 *  with a floor width. Used by `Dropdown`, `Select`, and the `Tab` actions menu so the three stay
 *  visually identical from a single definition.
 *
 *  The width is {@link MENU_PANEL_MIN_WIDTH_PX} written out — see there for why it is written out
 *  rather than interpolated, and for the test that keeps the two agreeing. The box is interpolated
 *  as WHOLE tokens, which Tailwind's scanner does see, because the literals appear above. */
export const MENU_PANEL_CLASS = `min-w-[180px] ${MENU_PANEL_BOX_CLASS}`;

/**
 * A menu row drawn as an INSET PILL rather than an edge-to-edge band (#2115, #2455) — the shape the
 * row's hover, its keyboard highlight and its selected tint all paint in. `DropdownOptionRow`,
 * `DropdownItem` and `ListButton`'s `menu` appearance carry it themselves, so a picker built on any of
 * them gets the pill without asking; it is exported for a row that is none of those.
 *
 * Why a pill: a full-width hover fill squares off against a rounded panel, and clipping the fill to
 * the panel's radius does not fix it — the panel's inset stops the band short of any corner it could
 * be clipped by. The row carrying the radius is the only version that looks right whether it is
 * first, last, or alone.
 */
export const MENU_ROW_CLASS = 'rounded-lg';

/** What an unselected menu row lights up with under the pointer: `row-hover`, in every menu. */
export const MENU_ROW_HOVER_CLASS = 'hover:bg-row-hover';

/**
 * The keyboard-highlighted row (a combobox's arrow-key candidate). The same fill as the hover, so the
 * row a key has reached reads exactly like the row the pointer is on.
 */
export const MENU_ROW_HIGHLIGHT_CLASS = 'bg-row-hover';

/**
 * A row that takes keyboard focus ITSELF — `Checkbox`'s `row` variant — lit with the same fill when a key
 * reaches it, so the row the keyboard is on reads like the row the pointer is on. The shared focus ring
 * (`styles.css`) is still drawn over it, as on every focusable menu row, so focus can be told apart from a
 * hover elsewhere. Written out whole, beside the fill it repeats, because Tailwind finds class names in the
 * source text and cannot see one built by interpolation.
 */
export const MENU_ROW_FOCUS_HIGHLIGHT_CLASS = 'focus-visible:bg-row-hover';

/** The committed selection's tint. It replaces the hover rather than joining it. */
export const MENU_ROW_SELECTED_CLASS = 'bg-brand-9/10 dark:bg-brand-9/20';

/**
 * What a menu row is painted with in each state: the pill, the keyboard highlight, and either the
 * selected tint or the hover — no hover on a row that cannot act, since a row that cannot act must not
 * light up under the pointer. `DropdownOptionRow` and `ListButton`'s `menu` appearance both paint
 * through this, so the two cannot disagree.
 */
export function menuRowClass({ selected = false, highlighted = false, canAct = true }: {
  selected?: boolean;
  highlighted?: boolean;
  canAct?: boolean;
}): string {
  return cn(
    MENU_ROW_CLASS,
    highlighted && MENU_ROW_HIGHLIGHT_CLASS,
    selected ? MENU_ROW_SELECTED_CLASS : canAct && MENU_ROW_HOVER_CLASS,
  );
}

/**
 * The rule between two groups of menu rows. It draws across the panel's CONTENT box, so the panel's
 * inset puts its ends exactly under the pills' edges — inset to match them, with no margin of its own.
 */
export const MENU_SEPARATOR_CLASS = 'my-1 border-t border-border';

/**
 * The horizontal half of the panel's inset, for a list that has to take it on its own box rather than
 * on the panel's: the assistant's `@` picker puts it on its list, so the legend beneath keeps a
 * full-width rule. The same step as {@link MENU_PANEL_INSET_CLASS}; prefer the panel's box wherever
 * the panel holds nothing but rows.
 */
export const MENU_ROW_INSET_CLASS = 'px-1';

/**
 * How wide the tooltip behind a blocked row's ⓘ is allowed to run.
 *
 * <b>One number for every such tooltip, rather than a knob per menu.</b> The reasons differ wildly in
 * length — "This row is already the last one." against the sign-blocked sentence, which names files
 * and the remedy for them — and a per-caller cap would draw the same control as a chip in one menu
 * and a paragraph in the next. A single cap means a long reason wraps to more lines rather than to a
 * different shape.
 *
 * Wider than `Tooltip`'s usual 260 because the longest reason in the product is two sentences: at 260
 * it ran to eight lines, which reads as a wall rather than as an answer.
 */
export const DISABLED_REASON_TOOLTIP_MAX_WIDTH = 300;
