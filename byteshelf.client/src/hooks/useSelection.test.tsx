// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useSelection, type Selection } from './useSelection';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const PLAIN = { shiftKey: false, metaKey: false, ctrlKey: false };
const SHIFT = { shiftKey: true, metaKey: false, ctrlKey: false };
const META = { shiftKey: false, metaKey: true, ctrlKey: false };
const CTRL = { shiftKey: false, metaKey: false, ctrlKey: true };

const ROWS = ['a', 'b', 'c', 'd'];

describe('useSelection', () => {
  let root: Root;
  let selection: Selection;

  function Probe({ ids, scope }: { ids: string[]; scope: string }) {
    selection = useSelection(ids, scope);
    return null;
  }

  function render(ids: string[] = ROWS, scope = 'shelf/'): void {
    act(() => root.render(<Probe ids={ids} scope={scope} />));
  }

  function selected(): string[] {
    return [...selection.selectedIds].sort();
  }

  beforeEach(() => {
    root = createRoot(document.createElement('div'));
    render();
  });

  afterEach(() => {
    act(() => root.unmount());
  });

  it('selects only the clicked row on a plain click', () => {
    act(() => selection.handleSelectClick('a', PLAIN));
    act(() => selection.handleSelectClick('c', PLAIN));

    expect(selected()).toEqual(['c']);
  });

  it('toggles a row on a cmd or ctrl click, keeping the others', () => {
    act(() => selection.handleSelectClick('a', META));
    act(() => selection.handleSelectClick('c', CTRL));
    expect(selected()).toEqual(['a', 'c']);

    act(() => selection.handleSelectClick('a', META));
    expect(selected()).toEqual(['c']);
  });

  it('selects the range from the last clicked row on a shift click, either way and redrawn from the same row', () => {
    act(() => selection.handleSelectClick('b', PLAIN));
    act(() => selection.handleSelectClick('d', SHIFT));
    expect(selected()).toEqual(['b', 'c', 'd']);

    act(() => selection.handleSelectClick('a', SHIFT));
    expect(selected()).toEqual(['a', 'b']);
  });

  it('selects just the row on a shift click with nothing clicked before', () => {
    act(() => selection.handleSelectClick('c', SHIFT));

    expect(selected()).toEqual(['c']);
  });

  it('selects every row, and clears', () => {
    act(() => selection.selectAll());
    expect(selected()).toEqual(ROWS);
    expect(selection.count).toBe(4);

    act(() => selection.clear());
    expect(selection.active).toBe(false);
  });

  it('toggles one row on Space', () => {
    act(() => selection.handleSelectClick('a', PLAIN));
    act(() => selection.toggle('b'));
    expect(selected()).toEqual(['a', 'b']);

    act(() => selection.toggle('a'));
    expect(selected()).toEqual(['b']);
  });

  it('drops a row that is no longer listed', () => {
    act(() => selection.selectAll());
    render(['a', 'c']);

    expect(selected()).toEqual(['a', 'c']);
    expect(selection.isSelected('b')).toBe(false);
  });

  it('starts empty in another scope, and does not bring the old selection back on return', () => {
    act(() => selection.selectAll());
    render(ROWS, 'shelf/2025');
    expect(selection.active).toBe(false);

    render(ROWS, 'shelf/');
    expect(selection.active).toBe(false);
  });
});
