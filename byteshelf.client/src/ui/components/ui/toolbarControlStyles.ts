/**
 * The size a control in a document toolbar is drawn at, written once (#2313).
 *
 * <b>Three components have to agree on this number, and each one used to pick it.</b> The group's
 * shell said `h-8`, the icon button inside it said nothing and was stretched to whatever the shell
 * left over, and the style dropdown said `py-2` and came out six pixels taller than both. A toolbar
 * is a row of boxes whose tops and bottoms are meant to line up, so a number three components each
 * choose is a number they will eventually choose differently — which is what a reader sees as a
 * ragged strip. It lives in `@ordo/ui` rather than beside the toolbar because one of the three
 * readers is `Select`, which is a shared primitive: a constant in `ordo.client` would be one the
 * component that most needs it cannot import.
 *
 * <b>The outline is a RING, and that is load-bearing rather than a style preference.</b> A `border`
 * takes its width out of the box's content, so a 32px box with a hairline leaves 30px inside it —
 * and a control that states the shared height then overflows its own group by a pixel at each end.
 * A ring is painted and occupies no space in the box model, so the box is 32px outside AND 32px
 * inside, and a control filling it can state the same height the box does. That is what lets all
 * three read one constant instead of two-thirds of them reading it and the last taking what is left.
 */

/** The outer height of any control in a document toolbar. */
export const TOOLBAR_CONTROL_HEIGHT_CLASS = 'h-8';

/**
 * The same rung on the other axis, for a control that is square — an icon button, and therefore the
 * segment it occupies. Kept beside the height rather than derived from it because Tailwind scans for
 * whole class names in source text and cannot see one built by interpolation, so each has to be
 * written out; the point of writing them adjacent is that a reader changing one sees the other.
 */
export const TOOLBAR_CONTROL_WIDTH_CLASS = 'w-8';

/**
 * The box a toolbar control — or a GROUP of them — draws around itself: the shared height, the shared
 * corner, and the hairline around it.
 *
 * Carries no padding. What sits inside a box differs per control — a segmented group insets nothing so
 * that its own inset does not sit outside the first and last segments, a pill insets its arrows — and
 * a padding here would be one every caller had to override, which `cn` cannot do: it concatenates, so
 * `px-1` and `px-0` would both survive and stylesheet order would decide which the reader saw.
 *
 * <b>KNOWN DEFECT: the `ring-inset` here is inherited by a caller's FOCUS ring.</b>
 * `--tw-ring-inset` is one custom property with no variant scope of its own, and Tailwind draws a
 * focus ring from the same variables this hairline uses — so on a focusable caller whose focus style
 * is `focus-visible:ring-2 ring-offset-2`, which is every `Button`, the focus indicator paints
 * INSIDE the control's own box, over its label, instead of around it. `Select`'s toolbar trigger has
 * carried this since it adopted the constant; `ReportDocumentSaveControls` joined it in #2324, where
 * Save moved from `variant="outline"` (whose focus ring was outset) onto this box.
 *
 * <b>The fix is to stop drawing the hairline as a ring</b> — `shadow-[inset_0_0_0_1px_…]` is
 * visually identical, takes no layout either, and never touches `--tw-ring-inset`, so a caller's
 * focus ring goes back to being outset. Deliberately NOT done in #2324: it changes how every
 * consumer of this constant draws its edge, and those had just been approved by eye. The scoped
 * reset that would avoid that, `focus-visible:[--tw-ring-inset:_]`, does not work — Tailwind drops
 * the empty arbitrary value and emits no rule at all, which was confirmed against built CSS rather
 * than assumed.
 *
 * <b>The corner is `!`, and that is what makes this constant authoritative rather than advisory.</b>
 * Every consumer so far is an element with no radius of its own, where the mark changes nothing. A
 * `Button` is not: its size step states `rounded-lg`, `cn` concatenates rather than merging, and
 * Tailwind emits the radius scale in size order — so the LARGER rule wins whatever order the classes
 * are written in, and a toolbar control reading this would have silently kept the button's corner
 * instead. A shared constant that loses to the thing it is meant to override is worse than no
 * constant, because it reads as applied.
 */
export const TOOLBAR_CONTROL_BOX_CLASS =
  `${TOOLBAR_CONTROL_HEIGHT_CLASS} !rounded-md ring-1 ring-inset ring-border`;

/**
 * The glyph size every control in a document toolbar draws at.
 *
 * Here beside the height and the box because it is the same subject — how big a toolbar control is —
 * and a third number kept in a file of its own is how the three come to disagree. `ToolbarButton`
 * takes this as its default rather than `IconButton`'s `size="sm"` 16px, so a control gets it by
 * being in the toolbar rather than by asking.
 */
export const TOOLBAR_ICON_SIZE_PX = 14;

/**
 * How much of its 24-unit artboard an icon's ink actually uses, for the two kinds this toolbar draws.
 *
 * Measured with `getBBox` in Chromium, not estimated: undo and redo carry 16 units of ink from top to
 * bottom, the B / I / U letterforms carry 18. They are named because they are the reason the two
 * sizes below differ — a bare `- 2` would read as a nudge somebody liked, and the next person
 * changing the toolbar size would have no way to tell whether it still applied.
 */
const ARROW_INK_UNITS = 16;
const LETTERFORM_INK_UNITS = 18;

/**
 * What a letterform icon draws at so its ink MEASURES the same as an arrow's beside it.
 *
 * <b>Derived rather than typed, because the correction is a ratio and not a constant.</b> Two icons
 * at one pixel size are only the same size on paper: the fuller artboard renders visibly bigger, which
 * is the mismatch a reader sees as a ragged strip. Scaling by the ink each one carries cancels that,
 * and it keeps doing so if {@link TOOLBAR_ICON_SIZE_PX} moves again — at 16 this yields 14, at 14 it
 * yields 12, which is both sizes this toolbar has been asked for so far.
 */
export const TOOLBAR_LETTERFORM_ICON_SIZE_PX = Math.round(
  (TOOLBAR_ICON_SIZE_PX * ARROW_INK_UNITS) / LETTERFORM_INK_UNITS,
);
