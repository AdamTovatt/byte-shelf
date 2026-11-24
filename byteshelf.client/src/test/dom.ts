import { Fragment, act, createElement, type ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { expect, vi } from 'vitest';

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

/** The button labelled `label`, as one that shows an icon is, or null when none shows. */
export function labelled(label: string): HTMLButtonElement | null {
  return document.body.querySelector(`button[aria-label="${label}"]`);
}

/** The first element `selector` finds, once there is one. */
export function untilFound<T extends Element = HTMLElement>(selector: string): Promise<T> {
  return vi.waitFor(async () => {
    await act(async () => {});
    const found = document.body.querySelector<T>(selector);
    expect(found).not.toBeNull();
    return found!;
  });
}

/** Waits until the page says `text`. */
export function shows(text: string): Promise<void> {
  return vi.waitFor(async () => {
    await act(async () => {});
    expect(document.body.textContent).toContain(text);
  });
}

/** The text a message reads as, rendered: a toast's, which may be a string or elements. */
export function textOf(message: ReactNode): string {
  const container = document.createElement('div');
  container.innerHTML = renderToStaticMarkup(createElement(Fragment, null, message));
  return container.textContent ?? '';
}

/** Presses the button whose text is `name`. */
export async function press(name: string): Promise<void> {
  const found = button(name);
  if (!found) throw new Error(`No button ${name}.`);
  await act(async () => found.click());
}

/** Types `password` into the one password prompt shown, and submits it. */
export async function submitPassword(password: string): Promise<void> {
  fill('Password', password);
  await act(async () => document.querySelector('form')!.requestSubmit());
}
