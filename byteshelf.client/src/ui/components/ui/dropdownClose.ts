import { createContext, useContext } from 'react';

/**
 * Carries the close callback of the surrounding {@link Dropdown} down to its panel content.
 *
 * Its own module rather than a member of `Dropdown.tsx` because that file exports components, and
 * `react-refresh/only-export-components` refuses a non-component export beside them — fast refresh
 * cannot re-run a mixed module without discarding the component state it also holds.
 */
export const DropdownCloseContext = createContext<(() => void) | null>(null);

/**
 * Closes the dropdown the calling element is rendered inside, for panel content that is not a
 * `DropdownItem` — a `Button`, a form — and so cannot inherit the item's own close-on-press.
 *
 * Reads a context provided around the panel's children, so it has to be called from a component
 * rendered *inside* the panel: called from the component that renders the `Dropdown` it sits above
 * the provider and returns `null`. Returning `null` rather than throwing is deliberate, so the same
 * component can render both inside a dropdown and outside one.
 */
export function useDropdownClose(): (() => void) | null {
  return useContext(DropdownCloseContext);
}
