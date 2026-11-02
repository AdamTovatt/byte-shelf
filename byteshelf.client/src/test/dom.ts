import { act } from 'react';

// Driving rendered forms as a person does, by the labels and button text they read. Dialogs portal
// into document.body, so everything is looked up there.

/** The field labelled `label`. */
export function field(label: string): HTMLInputElement {
  const labelElement = Array.from(document.body.querySelectorAll('label')).find((each) => each.textContent?.trim() === label);
  const input = labelElement && document.getElementById(labelElement.htmlFor);
  if (!(input instanceof HTMLInputElement)) throw new Error(`No field labelled ${label}.`);
  return input;
}

/** Types `value` into the field labelled `label`, as React sees typing. */
export function fill(label: string, value: string): void {
  const input = field(label);
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

/** The button whose text is `name`, or null when none shows. */
export function button(name: string): HTMLButtonElement | null {
  return Array.from(document.body.querySelectorAll('button')).find((each) => each.textContent?.trim() === name) ?? null;
}

/** Presses the button whose text is `name`. */
export async function press(name: string): Promise<void> {
  const found = button(name);
  if (!found) throw new Error(`No button ${name}.`);
  await act(async () => found.click());
}
