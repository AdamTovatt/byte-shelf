import { useState } from 'react';
import { PointerSensor, useSensor, useSensors, type Announcements, type DragEndEvent, type DragStartEvent, type UniqueIdentifier } from '@dnd-kit/core';
import { folderOfDropId } from '../lib/dropTargets';
import { ROOT_PATH, nameOf } from '../lib/shelfPaths';
import { runOrToast } from '../lib/utils';
import type { Entry } from '../services/shelfService';
import { useMoveEntries } from './useShelfQueries';

interface EntryDragAndDropArgs {
  shelf: string;
  /** The rows of the folder shown. */
  entries: Entry[];
  /** The selected rows: dragging one of them moves them all. */
  selected: Entry[];
}

/**
 * Moving rows of a folder by dragging them onto a folder row or a breadcrumb crumb, as in ordo's file
 * list: dragging a selected row moves the whole selection, an unselected row moves itself. A short
 * distance before a drag starts lets a click still select and a double-click still open. A refusal,
 * such as a plain file into an encrypted folder, shows as a toast; what moved before it stays moved.
 */
export function useEntryDragAndDrop({ shelf, entries, selected }: EntryDragAndDropArgs) {
  const moveEntries = useMoveEntries(shelf);
  const [dragged, setDragged] = useState<Entry[] | null>(null);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));
  const draggedIds = new Set((dragged ?? []).map((entry) => entry.value.id));

  /** What dragging the row `id` moves: the selection when the row is in it, else the row. */
  function draggedWith(id: UniqueIdentifier): Entry[] {
    const entry = entries.find((each) => each.value.id === String(id));
    if (!entry) return [];
    return selected.some((each) => each.value.id === entry.value.id) ? selected : [entry];
  }

  function describe(moving: Entry[]): string {
    return moving.length === 1 ? moving[0].value.name : `${moving.length} items`;
  }

  /** The name of the folder a drop target is, the shelf's for its root; null for none. */
  function folderName(id: UniqueIdentifier | undefined): string | null {
    const folder = folderOfDropId(id);
    return folder === null ? null : folder === ROOT_PATH ? shelf : nameOf(folder);
  }

  // What a screen reader hears, in names rather than dnd-kit's ids.
  const announcements: Announcements = {
    onDragStart: ({ active }) => `Picked up ${describe(draggedWith(active.id))}.`,
    onDragOver: ({ over }) => (folderName(over?.id) === null ? 'Over no folder.' : `Over ${folderName(over?.id)}.`),
    onDragEnd: ({ over }) => (folderName(over?.id) === null ? 'Dropped outside any folder; nothing moved.' : `Dropped into ${folderName(over?.id)}.`),
    onDragCancel: () => 'Cancelled; nothing moved.',
  };

  function onDragStart(event: DragStartEvent): void {
    const moving = draggedWith(event.active.id);
    if (moving.length > 0) setDragged(moving);
  }

  async function onDragEnd(event: DragEndEvent): Promise<void> {
    const moving = dragged;
    setDragged(null);
    const folder = folderOfDropId(event.over?.id);
    if (!moving || folder === null) return;
    await runOrToast(() => moveEntries(moving, folder), `Could not move ${describe(moving)}`);
  }

  return {
    sensors,
    announcements,
    /** What is being dragged; null when nothing is. */
    dragged,
    /** Whether a row can take the drop: a folder that is not itself being dragged. */
    takesDrop: (entry: Entry) => entry.kind === 'directory' && !draggedIds.has(entry.value.id),
    onDragStart,
    onDragEnd: (event: DragEndEvent) => void onDragEnd(event),
    onDragCancel: () => setDragged(null),
  };
}
