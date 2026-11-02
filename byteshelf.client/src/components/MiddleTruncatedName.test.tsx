// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MiddleTruncatedName, NameAndReason } from './MiddleTruncatedName';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const NAME = 'quarterly-report-final-2026.pdf';

describe('a name cut in its middle', () => {
  let root: Root;
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement('div');
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
  });

  it('keeps the whole name in its text, for a screen reader, and in its tooltip', () => {
    act(() => root.render(<MiddleTruncatedName name={NAME} />));

    const shown = container.firstElementChild!;
    expect([shown.textContent, shown.getAttribute('title')]).toEqual([NAME, NAME]);
  });

  it('sets the reason on a line below the name', () => {
    act(() => root.render(<NameAndReason before="Could not delete" name={NAME} reason="It is in use." />));

    const lines = Array.from(container.firstElementChild!.children, (line) => line.textContent);
    expect(lines).toEqual([`Could not delete ${NAME}:`, 'It is in use.']);
  });
});
