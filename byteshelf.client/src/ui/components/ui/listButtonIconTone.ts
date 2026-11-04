/**
 * The icon-slot colour vocabulary for `ListButton`, kept out of the component file so it can be
 * imported without pulling React in — the Tailwind preset's test asserts these classes are real
 * palette tokens, and would otherwise have to import a component to do it.
 */
export type ListButtonIconTone = 'muted' | 'inherit';

/**
 * Tone to utility class. `muted` is `neutral-11`, the app's one secondary ink; `inherit` contributes
 * no class, so the icon takes the colour of whatever encloses it.
 *
 * There used to be a third tone, `tertiary`, resolving to the raw `neutral-10` step. It went with
 * that step: two names one shade apart is how a row icon ends up a different grey from the row's own
 * text for no reason anyone could state. A caller that wants a row icon to stand apart from its
 * label now says so with `inherit` and colours the element itself, which is legible at the call site.
 *
 * **The union survived the deletion on purpose**, rather than collapsing to a `colorIcon` boolean:
 * `iconTone="inherit"` states a ROLE at the call site — "this row drives its own icon colour" — where
 * `colorIcon={false}` would state a mechanism and leave the reader to work out why. The map is also
 * read by `tailwind/colors.test.js`, which checks every class here is a real palette token; a boolean
 * would have no class to check, and the `text-muted` literal would move into the component beside
 * React, which is what this module exists to keep it away from.
 *
 * These literals only work because the palette defines matching tokens — `text-muted` is a utility
 * solely because `muted` is a functional-alias key. Renaming the alias would stop Tailwind emitting
 * the class while every class-name assertion stayed green, so `tailwind/colors.test.js` ties the two
 * together.
 */
export const ICON_TONE_CLASS: Record<ListButtonIconTone, string | undefined> = {
  muted: 'text-muted',
  inherit: undefined,
};

// The `text-*` utilities that are NOT colours. Everything else after `text-` sets a colour, so the
// predicate below is framed as "not one of these": colours are an open set (this palette's hues and
// aliases, Tailwind's own default palette, arbitrary values like `text-[#abc]`, and `text-inherit`),
// while this list is small, closed and effectively frozen by Tailwind. Sizes may carry a line-height
// suffix (`text-sm/6`), so one is allowed after any of them.
//
// Known and accepted: an arbitrary SIZE (`text-[14px]`) is treated as a colour, because arbitrary
// values are indistinguishable by name. Dev-only, no caller writes one, and the inverse error — a
// real colour going unwarned — is the one that costs something.
const NON_COLOR_TEXT_UTILITY =
  /^(xs|sm|base|lg|[2-9]?xl|left|center|right|justify|start|end|ellipsis|clip|wrap|nowrap|balance|pretty|opacity(-\d+)?|body-.+|heading-.+)(\/[\w.]+)?$/;

/**
 * True when `className` carries a colour utility. Variant-prefixed classes (`hover:text-fg`) are
 * deliberately excluded: a variant layers a colour over the base tone for one state rather than
 * competing with it, which is a legitimate thing to pass.
 */
export function carriesColorClass(className: string): boolean {
  return className
    .split(/\s+/)
    .filter((name) => name.length > 0 && !name.includes(':'))
    .map((name) => name.replace(/^!/, ''))
    .some((name) => name.startsWith('text-') && !NON_COLOR_TEXT_UTILITY.test(name.slice('text-'.length)));
}
