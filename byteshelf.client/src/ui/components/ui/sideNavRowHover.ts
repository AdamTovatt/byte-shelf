/**
 * The fill a sidebar row takes under the pointer: `surface`, the fill a selected `SideNavItem` rests
 * on. Exported for a sidebar control that is not a `SideNavItem` (the Archived toggle in `SideNav`),
 * so that no row in the rail can hover with a different grey — the darker hover came back once
 * already by a fill being edited in one place and not the other.
 */
export const SIDE_NAV_ROW_HOVER = 'hover:bg-surface';
