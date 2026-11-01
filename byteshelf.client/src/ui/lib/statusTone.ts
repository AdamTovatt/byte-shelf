/**
 * The design system's one status-tone vocabulary: the six names every surface that reports a state
 * uses, and the classes each resolves to per surface shape.
 *
 * One name and one map because the alternative is what this replaced — `Badge` owning the union
 * privately while every other status surface re-declared its own subset. A subset is how one surface
 * ends up a colour behind the others after a retune, and nothing fails when it does: both spellings
 * are valid classes, they just disagree.
 *
 * These are the STATUS hues, which report a verdict. Decoration that merely labels a thing takes a
 * `playful` concept hue instead — see `ColorSystem.md`, "Playful (decorative accents)", for the
 * division and why a green that meant both would be unreadable.
 */
export const STATUS_TONES = ['default', 'brand', 'success', 'warning', 'danger', 'info'] as const;

/**
 * Derived from the list above rather than written twice, so a surface can iterate every tone — which
 * is how a test pins that a shape covers the whole vocabulary instead of the four tones it happened
 * to be written with.
 */
export type StatusTone = (typeof STATUS_TONES)[number];

/**
 * Dot-sized fill for a status: a small solid circle, so it takes step 9 (the solid step) rather than
 * an alpha wash.
 *
 * Written out per tone rather than composed as `bg-${tone}-9` because Tailwind scans source text and
 * emits only complete literal class strings — an interpolated one produces no CSS at all.
 */
const DOT_COLOR: Record<StatusTone, string> = {
  success: 'bg-success-9',
  danger: 'bg-danger-9',
  warning: 'bg-warning-9',
  info: 'bg-info-9',
  default: 'bg-neutral-8',
  brand: 'bg-brand-9',
};

/**
 * Dot-sized ring for a status: the hollow form of {@link DOT_COLOR}'s dot, same step, for a state still waiting on
 * someone beside the solid dots of those decided (the QC run overview's need review, #2711).
 */
const RING_COLOR: Record<StatusTone, string> = {
  success: 'border-success-9',
  danger: 'border-danger-9',
  warning: 'border-warning-9',
  info: 'border-info-9',
  default: 'border-neutral-8',
  brand: 'border-brand-9',
};

/** The ring colour for a status tone, for `ActivityDot`'s hollow form. */
export function statusRingClass(tone: StatusTone): string {
  return RING_COLOR[tone];
}

/** The dot fill for a status tone. Used by `ActivityDot` and by any pip drawn on another element. */
export function statusDotClass(tone: StatusTone): string {
  return DOT_COLOR[tone];
}

/**
 * Words in a status's colour: the step-11 text of its hue, the one that reads on every surface — or the
 * muted ink for `default`, which reports no verdict. For a state written out beside its dot rather than
 * filled into a pill, so the words and the dot agree.
 *
 * Written out per tone for the reason {@link DOT_COLOR} is.
 */
const TEXT_COLOR: Record<StatusTone, string> = {
  success: 'text-success-11',
  danger: 'text-danger-11',
  warning: 'text-warning-11',
  info: 'text-info-11',
  default: 'text-muted',
  brand: 'text-brand-11',
};

/** The text colour for a status tone. */
export function statusTextClass(tone: StatusTone): string {
  return TEXT_COLOR[tone];
}
