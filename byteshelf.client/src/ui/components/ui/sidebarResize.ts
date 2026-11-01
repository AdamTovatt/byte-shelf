/**
 * Pure per-mousemove decision logic for the {@link Sidebar} resize handle.
 *
 * Kept framework-free and separate from `Sidebar.tsx` so it is unit-testable and so the
 * event handler stays thin. Because the result is derived solely from the immutable
 * drag-start values (`startedCollapsed` / `startWidth`) plus the current pointer delta, it
 * can be re-evaluated fresh on every move — collapse↔expand transitions within a single drag
 * resolve correctly without any mutable drag-anchor state.
 */

import { clamp } from '../../lib/utils';

export interface ResolveSidebarDragArgs {
  /** Pointer X minus the X at mousedown, in pixels. */
  delta: number;
  /** Collapse state captured at mousedown. */
  startedCollapsed: boolean;
  /** Sidebar width captured at mousedown. Only used when `startedCollapsed` is false. */
  startWidth: number;
  minWidth: number;
  maxWidth: number;
  /** Whether dragging an expanded sidebar narrow enough collapses it. */
  autoCollapseOnDrag: boolean;
  /** Pixels below `minWidth` before auto-collapse fires. */
  autoCollapseOffset: number;
  /** Pixels the pointer must travel right before a collapsed sidebar expands. */
  expandDragThreshold: number;
}

export interface SidebarDragResult {
  collapsed: boolean;
  /** Target width, or `null` to leave the current width unchanged. */
  width: number | null;
}

/** Resolve the next collapse/width state for a single resize-drag move event. */
export function resolveSidebarDrag(args: ResolveSidebarDragArgs): SidebarDragResult {
  const {
    delta,
    startedCollapsed,
    startWidth,
    minWidth,
    maxWidth,
    autoCollapseOnDrag,
    autoCollapseOffset,
    expandDragThreshold,
  } = args;

  if (startedCollapsed) {
    // Stay collapsed until the pointer is pulled past the threshold; then snap open to
    // minWidth and track the cursor 1:1 (the `- expandDragThreshold` offset anchors the
    // expanded edge to the pointer position where it crossed the threshold).
    if (delta <= expandDragThreshold) {
      return { collapsed: true, width: null };
    }
    return {
      collapsed: false,
      width: clamp(minWidth + (delta - expandDragThreshold), minWidth, maxWidth),
    };
  }

  const newWidth = startWidth + delta;

  if (autoCollapseOnDrag && newWidth < minWidth - autoCollapseOffset) {
    return { collapsed: true, width: null };
  }

  return { collapsed: false, width: clamp(newWidth, minWidth, maxWidth) };
}
