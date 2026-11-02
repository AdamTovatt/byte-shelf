import { useCallback, useMemo, useState } from 'react';

/** The modifier keys read off a row click to decide replace, toggle or range selection. */
export interface SelectionClickModifiers {
  shiftKey: boolean;
  metaKey: boolean;
  ctrlKey: boolean;
}

export interface Selection {
  selectedIds: ReadonlySet<string>;
  count: number;
  /** True when at least one row is selected. */
  active: boolean;
  isSelected: (id: string) => boolean;
  clear: () => void;
  /** Adds or removes one row, leaving the others (Space on a row). */
  toggle: (id: string) => void;
  /**
   * A row's click, as Finder reads it: shift extends a range from the last row clicked, cmd or ctrl
   * toggles the row, and a plain click selects only that row.
   */
  handleSelectClick: (id: string, modifiers: SelectionClickModifiers) => void;
  selectAll: () => void;
}

interface Stored {
  scope: string;
  ids: ReadonlySet<string>;
  /** The last row a click or toggle touched: the fixed end of a shift range. */
  anchor: string | null;
}

/**
 * Finder-style multi-selection over rows listed in `orderedIds`, in display order. A selection
 * belongs to its `scope` (such as the shelf and folder shown), so it is gone once the scope changes
 * rather than coming back on return; rows no longer listed drop out of it. Copied from ordo's
 * useFileSelection, without the folder tree it also serves there.
 */
export function useSelection(orderedIds: string[], scope: string): Selection {
  const [stored, setStored] = useState<Stored>({ scope, ids: new Set(), anchor: null });
  // Dropped as the scope changes, during the render rather than in an effect: React renders again at
  // once with the empty selection, so the old one never shows in the new scope and is not there to
  // come back.
  if (stored.scope !== scope) setStored({ scope, ids: new Set(), anchor: null });

  // Derived rather than pruned in an effect, so a deleted row never counts for a render.
  const listed = useMemo(() => new Set(orderedIds), [orderedIds]);
  const selectedIds = useMemo(() => {
    const kept = [...stored.ids].filter((id) => listed.has(id));
    return kept.length === stored.ids.size ? stored.ids : new Set(kept);
  }, [stored.ids, listed]);

  const update = useCallback(
    (next: (previous: Stored) => Omit<Stored, 'scope'>) => setStored((previous) => ({ ...previous, ...next(previous) })),
    [],
  );

  const clear = useCallback(() => update(() => ({ ids: new Set(), anchor: null })), [update]);

  const toggle = useCallback((id: string) => update((previous) => ({ ids: toggled(previous.ids, id), anchor: id })), [update]);

  const handleSelectClick = useCallback(
    (id: string, modifiers: SelectionClickModifiers) => {
      update((previous) => {
        if (modifiers.shiftKey && previous.anchor !== null) {
          const from = orderedIds.indexOf(previous.anchor);
          const to = orderedIds.indexOf(id);
          if (from !== -1 && to !== -1) {
            const [low, high] = from < to ? [from, to] : [to, from];
            // The anchor stays, so a second shift-click redraws the range from the same row.
            return { ids: new Set(orderedIds.slice(low, high + 1)), anchor: previous.anchor };
          }
        }
        if (modifiers.metaKey || modifiers.ctrlKey) return { ids: toggled(previous.ids, id), anchor: id };
        return { ids: new Set([id]), anchor: id };
      });
    },
    [orderedIds, update],
  );

  const selectAll = useCallback(
    () => update(() => ({ ids: new Set(orderedIds), anchor: orderedIds.at(-1) ?? null })),
    [orderedIds, update],
  );

  const isSelected = useCallback((id: string) => selectedIds.has(id), [selectedIds]);

  return { selectedIds, count: selectedIds.size, active: selectedIds.size > 0, isSelected, clear, toggle, handleSelectClick, selectAll };
}

function toggled(ids: ReadonlySet<string>, id: string): ReadonlySet<string> {
  const next = new Set(ids);
  if (!next.delete(id)) next.add(id);
  return next;
}
