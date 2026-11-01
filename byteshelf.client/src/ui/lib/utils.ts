type ClassValue = string | number | boolean | null | undefined | ClassDictionary | ClassArray;
type ClassDictionary = Record<string, boolean | undefined | null>;
type ClassArray = ClassValue[];

function appendClassNames(value: ClassValue, classes: string[]): void {
  if (!value) {
    return;
  }

  if (typeof value === 'string' || typeof value === 'number') {
    classes.push(String(value));
    return;
  }

  if (Array.isArray(value)) {
    for (const nestedValue of value) {
      appendClassNames(nestedValue, classes);
    }
    return;
  }

  for (const [key, isEnabled] of Object.entries(value)) {
    if (isEnabled) {
      classes.push(key);
    }
  }
}

/**
 * Joins class values into one string: arrays, objects (`{ 'is-open': open }`) and falsy entries, the
 * shape every component here passes.
 *
 * **It CONCATENATES. It does not merge, and that is the thing to know before writing a `className`
 * prop.** Two utilities from the same Tailwind group — `justify-center` and `justify-start`,
 * `text-black` and `text-landing-ink`, two `w-*` — both survive this function, so which one paints is
 * decided by the order Tailwind happened to emit them in the stylesheet, not by which was passed last.
 * A component's base class beating its caller's override is the usual symptom, and it resolves
 * "correctly" often enough to look like it works.
 *
 * Two ways out, in order of preference. Do not have the base state a property a caller is expected to
 * override — a property that is both overridden and unnecessary is worth deleting rather than
 * winning. Where the base must state it, the caller marks its own `!important` (`!justify-start`),
 * which is what several call sites across the apps do.
 *
 * This is deliberately not `tailwind-merge`: that resolves the conflict at runtime by parsing class
 * names, and the trade taken here is a smaller dependency surface and no parser to keep in step with
 * Tailwind's group list. The cost is that every caller has to know, and it was being paid by half a
 * dozen copies of this paragraph at call sites across `ordo.client` with nothing here. Several of
 * those copies remain and can be replaced by a pointer to this one; that sweep is not this change.
 */
export function cn(...inputs: ClassValue[]): string {
  const classes: string[] = [];

  for (const input of inputs) {
    appendClassNames(input, classes);
  }

  return classes.join(' ');
}

export function getErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error) {
    return error.message || fallback;
  }
  return fallback;
}

/** Constrain `value` to the inclusive `[min, max]` range. */
export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
