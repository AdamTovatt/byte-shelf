import {
  type ReactNode,
  type HTMLAttributes,
  createContext,
  useContext,
  useState,
  useCallback,
  useRef,
  useMemo,
} from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { IconButton } from './IconButton';
import { ScrollbarOverlay } from './ScrollbarOverlay';
import { iconMap } from '../../lib/iconMap';
import { springs } from '../../lib/springs';
import { cn } from '../../lib/utils';
import { resolveSidebarDrag } from './sidebarResize';

/* ------------------------------------------------------------------ */
/*  Context                                                            */
/* ------------------------------------------------------------------ */

interface SidebarStateContextValue {
  isCollapsed: boolean;
  width: number;
  toggle: () => void;
  expand: () => void;
  collapse: () => void;
}

const SidebarStateContext = createContext<SidebarStateContextValue | undefined>(undefined);

/** Read collapse / width state from the nearest `<Sidebar>`. */
// eslint-disable-next-line react-refresh/only-export-components
export function useSidebarState(): SidebarStateContextValue {
  const ctx = useContext(SidebarStateContext);
  if (!ctx) {
    throw new Error('useSidebarState must be used within a <Sidebar>');
  }
  return ctx;
}

/* ------------------------------------------------------------------ */
/*  Sidebar (root)                                                     */
/* ------------------------------------------------------------------ */

export interface SidebarProps {
  children: ReactNode;
  /** Content rendered when collapsed; crossfades with `children` via AnimatePresence. */
  collapsedChildren?: ReactNode;
  minWidth?: number;
  maxWidth?: number;
  collapsedWidth?: number;
  defaultWidth?: number;
  /** Controlled width. */
  width?: number;
  onWidthChange?: (w: number) => void;
  defaultCollapsed?: boolean;
  /** Controlled collapsed state. */
  collapsed?: boolean;
  onCollapsedChange?: (c: boolean) => void;
  /** Show the built-in resize handle. Default: true. */
  resizable?: boolean;
  /** Auto-collapse when dragged below minWidth. Default: false. */
  autoCollapseOnDrag?: boolean;
  /** Pixels below minWidth before auto-collapse fires. Default: 20. */
  autoCollapseOffset?: number;
  /** Pixels the right edge must be dragged before a collapsed sidebar expands. Default: 20. */
  expandDragThreshold?: number;
  /** Show the built-in collapse/expand toggle button. Default: true. */
  showCollapseToggle?: boolean;
  /** Animate opacity crossfade between children and collapsedChildren. Default: true. */
  crossfade?: boolean;
  /** Animate width transitions. When false, width changes are instant. Default: true. */
  animateWidth?: boolean;
  /** Force dark appearance regardless of the global theme. Default: false. */
  forceDark?: boolean;
  className?: string;
  role?: string;
}

/**
 * The rail's drag bounds and resting widths.
 *
 * Exported because a consuming app needs the same numbers to persist and restore a width — it has to reject a
 * stored value outside the range it will then hand back as `width`. Two apps had each written the four
 * constants next to their own persistence, so the same numbers lived in three places (here, and once per app)
 * with nothing tying them together.
 *
 * `SIDEBAR_MIN_WIDTH` deliberately differs from this component's own `minWidth` default: an app that persists
 * a width sets its own floor, and the default here is what an unconfigured rail gets.
 */
export const SIDEBAR_MIN_WIDTH = 180;
export const SIDEBAR_MAX_WIDTH = 400;
export const SIDEBAR_COLLAPSED_WIDTH = 64;
export const SIDEBAR_DEFAULT_WIDTH = 240;

export function Sidebar({
  children,
  collapsedChildren,
  minWidth = 200,
  maxWidth = SIDEBAR_MAX_WIDTH,
  collapsedWidth = SIDEBAR_COLLAPSED_WIDTH,
  defaultWidth = SIDEBAR_DEFAULT_WIDTH,
  width: controlledWidth,
  onWidthChange,
  defaultCollapsed = false,
  collapsed: controlledCollapsed,
  onCollapsedChange,
  resizable = true,
  autoCollapseOnDrag = false,
  autoCollapseOffset = 20,
  expandDragThreshold = 20,
  showCollapseToggle = true,
  crossfade = true,
  animateWidth = true,
  forceDark = false,
  className,
  role,
}: SidebarProps) {
  /* ---------- uncontrolled state ---------- */
  const [uncontrolledWidth, setUncontrolledWidth] = useState(defaultWidth);
  const [uncontrolledCollapsed, setUncontrolledCollapsed] = useState(defaultCollapsed);

  const isWidthControlled = controlledWidth !== undefined;
  const isCollapsedControlled = controlledCollapsed !== undefined;

  const currentWidth = isWidthControlled ? controlledWidth : uncontrolledWidth;
  const isCollapsed = isCollapsedControlled ? controlledCollapsed : uncontrolledCollapsed;

  const setWidth = useCallback((w: number) => {
    if (!isWidthControlled) setUncontrolledWidth(w);
    onWidthChange?.(w);
  }, [isWidthControlled, onWidthChange]);

  const setCollapsed = useCallback((c: boolean) => {
    if (!isCollapsedControlled) setUncontrolledCollapsed(c);
    onCollapsedChange?.(c);
  }, [isCollapsedControlled, onCollapsedChange]);

  const toggle = useCallback(() => setCollapsed(!isCollapsed), [isCollapsed, setCollapsed]);
  const expand = useCallback(() => setCollapsed(false), [setCollapsed]);
  const collapse = useCallback(() => setCollapsed(true), [setCollapsed]);

  /* ---------- resize ---------- */
  const isDragging = useRef(false);
  const rootRef = useRef<HTMLDivElement>(null);

  const handleResizeStart = useCallback((event: React.MouseEvent) => {
    event.preventDefault();
    isDragging.current = true;

    const startX = event.clientX;
    const startedCollapsed = isCollapsed;
    const startWidth = currentWidth;
    // Track the applied collapse state locally so we only call setCollapsed on an actual
    // transition (its context setter writes localStorage), rather than every move.
    let appliedCollapsed = startedCollapsed;

    // Disable spring during drag for immediate feedback
    const root = rootRef.current;
    if (root) root.style.transition = 'none';

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const result = resolveSidebarDrag({
        delta: moveEvent.clientX - startX,
        startedCollapsed,
        startWidth,
        minWidth,
        maxWidth,
        autoCollapseOnDrag,
        autoCollapseOffset,
        expandDragThreshold,
      });

      if (result.collapsed !== appliedCollapsed) {
        appliedCollapsed = result.collapsed;
        setCollapsed(result.collapsed);
      }
      if (result.width !== null) setWidth(result.width);
    };

    const handleMouseUp = () => {
      isDragging.current = false;
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
      if (root) root.style.transition = '';
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }, [currentWidth, isCollapsed, minWidth, maxWidth, autoCollapseOnDrag, autoCollapseOffset, expandDragThreshold, setCollapsed, setWidth]);

  /* ---------- context ---------- */
  const ctxValue = useMemo<SidebarStateContextValue>(() => ({
    isCollapsed,
    width: currentWidth,
    toggle,
    expand,
    collapse,
  }), [isCollapsed, currentWidth, toggle, expand, collapse]);

  /* ---------- render ---------- */
  const hasCollapsedChildren = collapsedChildren !== undefined;

  const sidebar = (
    <SidebarStateContext.Provider value={ctxValue}>
      <motion.div
        ref={rootRef}
        animate={{ width: isCollapsed ? collapsedWidth : currentWidth }}
        transition={animateWidth ? springs.stiff : { duration: 0 }}
        className={cn(
          'shrink-0 border-r border-border',
          'bg-panel',
          'overflow-hidden relative',
          forceDark && 'h-full',
          className,
        )}
        role={role}
      >
        {/* Inner wrapper at target width so content is revealed-in-place instead of reflowing */}
        <div
          className="flex flex-col h-full"
          style={{ width: isCollapsed ? collapsedWidth : currentWidth }}
        >
          {/* Built-in collapse toggle */}
          {showCollapseToggle && (
            <div className={cn(
              'h-12 flex items-center px-2 shrink-0',
              isCollapsed ? 'justify-center' : 'justify-end',
            )}>
              <IconButton
                variant="ghost"
                size="sm"
                icon={isCollapsed ? iconMap.chevronRight : iconMap.chevronLeft}
                onClick={toggle}
                aria-label={isCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
              />
            </div>
          )}

          {/* Content — crossfade when collapsedChildren is provided */}
          {hasCollapsedChildren && crossfade ? (
            <AnimatePresence mode="wait" initial={false}>
              {isCollapsed ? (
                <motion.div
                  key="collapsed"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.15 }}
                  className="flex flex-col flex-1 min-h-0"
                >
                  {collapsedChildren}
                </motion.div>
              ) : (
                <motion.div
                  key="expanded"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.15 }}
                  className="flex flex-col flex-1 min-h-0"
                >
                  {children}
                </motion.div>
              )}
            </AnimatePresence>
          ) : hasCollapsedChildren ? (
            <div className="flex flex-col flex-1 min-h-0">
              {isCollapsed ? collapsedChildren : children}
            </div>
          ) : (
            <div className="flex flex-col flex-1 min-h-0">
              {children}
            </div>
          )}
        </div>

        {/* Resize handle — shown when collapsed too, so the edge can be dragged to expand.
            Above `.scrollbar-overlay-track`'s z-index of 30, deliberately (#1979). A scrollable
            `SidebarSection` now renders an overlay track 3px from its right edge and 8px wide, which
            lands over 4 of this handle's 6px — and the ancestors between here and the track are all
            non-stacking-contexts, so at the old `z-20` the track won hit-testing and the rail's drag
            edge stopped resizing (and, where `autoCollapseOnDrag` is set, stopped collapsing) wherever
            the nav overflowed. The native scrollbar it replaced sat INSIDE the scroll element, below
            this handle, so this restores the ordering rather than inventing one. Nothing changes
            visually: the handle paints only on hover/active, so the track stays fully visible; the
            cost is that the thumb's rightmost 4px drag the rail instead of the scrollbar. */}
        {resizable && (
          <div
            className="absolute right-0 top-0 bottom-0 w-1.5 cursor-col-resize z-40 hover:bg-brand-tint/20 active:bg-brand-tint/30 transition-colors"
            onMouseDown={handleResizeStart}
          />
        )}
      </motion.div>
    </SidebarStateContext.Provider>
  );

  if (forceDark) {
    // `data-theme` picks the dark CSS-variable set; the `.dark` class is kept so Tailwind's `dark:`
    // variants still activate inside the subtree.
    return <div className="dark shrink-0" data-theme="dark">{sidebar}</div>;
  }

  return sidebar;
}

/* ------------------------------------------------------------------ */
/*  SidebarHeader                                                      */
/* ------------------------------------------------------------------ */

export interface SidebarHeaderProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
  /** Show a bottom border. Default: true. */
  border?: boolean;
}

export function SidebarHeader({ children, border = true, className, ...props }: SidebarHeaderProps) {
  return (
    <div
      className={cn(
        'shrink-0',
        border && 'border-b border-border',
        className,
      )}
      {...props}
    >
      {children}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  SidebarSection                                                     */
/* ------------------------------------------------------------------ */

export interface SidebarSectionProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
  /** Top border style. Default: false (no border). */
  separator?: false | 'full' | 'padded';
  /** Padding preset. Default: 'md'. */
  padding?: 'none' | 'sm' | 'md';
  /** When true, section grows and scrolls. Default: false. */
  scrollable?: boolean;
}

const sectionPadding: Record<NonNullable<SidebarSectionProps['padding']>, string> = {
  none: '',
  sm: 'px-2 py-2',
  md: 'px-4 py-4',
};

export function SidebarSection({
  children,
  separator = false,
  padding = 'md',
  scrollable = false,
  className,
  ...props
}: SidebarSectionProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const separatorClasses = cn(
    separator === 'full' && 'border-t border-border',
    separator === 'padded' && 'border-t border-border mx-4',
  );

  if (!scrollable) {
    return (
      <div className={cn(sectionPadding[padding], separatorClasses, className)} {...props}>
        {children}
      </div>
    );
  }

  // Scrollable mode paints the app's own overlay scrollbar rather than the browser's, so the rail
  // looks the same on every platform (#1979). Three nodes, each load-bearing:
  //
  //  - the wrapper owns the flex sizing, is the `relative` containing block the overlay's absolutely
  //    positioned track measures against, AND is the hover target — `useScrollbarOverlay` binds
  //    mouseenter/leave to `scrollRef.current.parentElement`;
  //  - the scroll element hides the native bar (`scrollbar-hidden`) and keeps the padding preset, as
  //    `ReportPageChromePanel` does with its own;
  //  - the content div holds the children, so the caller's `className` lays them out (below).
  //
  // The prop bag splits across the last two, and asymmetrically against non-scrollable mode:
  // `className` lands on the content div while `...props` stays on the scroll element. Both nav call
  // sites pass `space-y-1`, and `space-y-*` only spaces an element's DIRECT children, so putting it
  // on the scroll element would space the single content div against nothing. This is what keeps
  // `className` meaning "layout for my children" in both modes without a `contentClassName` prop or
  // a call-site change.
  //
  // The alternative — always render the content div, so `className` has ONE target — was weighed and
  // rejected in #1979 rather than overlooked: it changes the DOM of every non-scrollable section for
  // the benefit of the two scrollable ones. So is a `contentClassName` prop, which puts the same
  // choice at every call site instead of here. Do not re-open either without re-opening that issue.
  return (
    <div className={cn('relative flex-1 min-h-0', separatorClasses)}>
      <div
        ref={scrollRef}
        className={cn('h-full overflow-y-auto overflow-x-hidden scrollbar-hidden', sectionPadding[padding])}
        {...props}
      >
        <div className={className}>{children}</div>
      </div>
      {/* No `HORIZONTAL_SCROLLBAR_GUTTER`, and `overflow-x-hidden` is what earns that. `overflow-y-auto`
          alone would leave the x axis computing to `auto` (a `visible` paired with a non-`visible`
          resolves that way), so a row wider than the rail would float a horizontal track over the last
          item with nothing padding for it. Clipping is the right answer for a nav rail regardless —
          `SideNavItem` already truncates its label — and it makes the one-axis claim checkable rather
          than merely stated. */}
      <ScrollbarOverlay scrollRef={scrollRef} />
    </div>
  );
}

/* ------------------------------------------------------------------ */
/*  SidebarFooter                                                      */
/* ------------------------------------------------------------------ */

export interface SidebarFooterProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
  /** Show a top border. Default: true. */
  separator?: boolean;
}

export function SidebarFooter({ children, separator = true, className, ...props }: SidebarFooterProps) {
  return (
    <div
      className={cn(
        'shrink-0',
        separator && 'border-t border-border',
        className,
      )}
      {...props}
    >
      {children}
    </div>
  );
}
