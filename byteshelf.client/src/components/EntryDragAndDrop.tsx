import type { ReactNode } from 'react';
import { useDraggable, useDroppable } from '@dnd-kit/core';
import { Icon, cn, getFileIcon, iconMap } from '../ui';
import type { Entry } from '../services/shelfService';
import { folderDropId } from '../lib/dropTargets';

// Moving rows by dragging them, from ordo's file list: a row drags, a folder row or a folder's
// breadcrumb crumb takes the drop. dnd-kit works on pointer events, so it never meets the browser's
// own drag events that dropping files from the computer to upload them uses.

/** The outline a folder shows while what is dragged would drop into it. */
const DROP_OUTLINE = 'rounded-lg ring-1 ring-inset ring-selected bg-selected/10';

/**
 * Wraps a row so it can be dragged, and a folder's row so things drop into it too. Only the pointer
 * listeners are spread, not the keyboard attributes: the row already holds its own button, and
 * keyboard users move things through the row's Move.
 */
export function DraggableEntryRow({ entry, droppable, children }: { entry: Entry; droppable: boolean; children: ReactNode }) {
  const { listeners, setNodeRef: setDragRef, isDragging } = useDraggable({ id: entry.value.id });
  const { setNodeRef: setDropRef, isOver } = useDroppable({ id: folderDropId(entry.value.path), disabled: !droppable || entry.kind !== 'directory' });
  return (
    <div ref={setDropRef} data-active={isOver || undefined} className={cn(isOver && DROP_OUTLINE)}>
      <div ref={setDragRef} {...listeners} className={cn(isDragging && 'opacity-40')}>
        {children}
      </div>
    </div>
  );
}

/** A breadcrumb crumb things drop into, as into its folder; `disabled` for the folder shown. */
export function DroppableCrumb({ folderPath, disabled, children }: { folderPath: string; disabled: boolean; children: ReactNode }) {
  const { setNodeRef, isOver } = useDroppable({ id: folderDropId(folderPath), disabled });
  return (
    <div ref={setNodeRef} className={cn('-mx-1 px-1', isOver && DROP_OUTLINE)}>
      {children}
    </div>
  );
}

/** What follows the pointer while dragging: the one entry's icon and name, or how many there are. */
export function EntryDragPreview({ entries }: { entries: Entry[] }) {
  const single = entries.length === 1 ? entries[0] : null;
  const icon = single ? (single.kind === 'directory' ? iconMap.folder : getFileIcon(single.value.name)) : iconMap.file;
  return (
    <div className="flex max-w-xs items-center gap-2 rounded-lg border border-border bg-panel px-3 py-1.5 text-body-md text-fg shadow-lg">
      <Icon icon={icon} size={18} className="shrink-0 text-muted" />
      <span className="truncate">{single ? single.value.name : `${entries.length} items`}</span>
    </div>
  );
}
