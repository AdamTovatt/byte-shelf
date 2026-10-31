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
import { iconMap } from '../../lib/iconMap';
import { springs } from '../../lib/springs';
import { cn } from '../../lib/utils';

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

export function Sidebar({
  children,
  collapsedChildren,
  minWidth = 200,
  maxWidth = 400,
  collapsedWidth = 64,
  defaultWidth = 240,
  width: controlledWidth,
  onWidthChange,
  defaultCollapsed = false,
  collapsed: controlledCollapsed,
  onCollapsedChange,
  resizable = true,
  autoCollapseOnDrag = false,
  autoCollapseOffset = 20,
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
    const startWidth = currentWidth;

    // Disable spring during drag for immediate feedback
    const root = rootRef.current;
    if (root) root.style.transition = 'none';

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientX - startX;
      const newWidth = startWidth + delta;

      if (autoCollapseOnDrag && newWidth < minWidth - autoCollapseOffset) {
        if (!isCollapsed) setCollapsed(true);
      } else {
        if (autoCollapseOnDrag && isCollapsed) setCollapsed(false);
        setWidth(Math.min(maxWidth, Math.max(minWidth, newWidth)));
      }
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
  }, [currentWidth, isCollapsed, minWidth, maxWidth, autoCollapseOnDrag, autoCollapseOffset, setCollapsed, setWidth]);

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
          'shrink-0 border-r border-border-light dark:border-border-dark',
          'bg-elevation-2-light dark:bg-elevation-2-dark',
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

        {/* Resize handle */}
        {resizable && !isCollapsed && (
          <div
            className="absolute right-0 top-0 bottom-0 w-1.5 cursor-col-resize z-20 hover:bg-brand-tint-light/20 dark:hover:bg-brand-tint-dark/20 active:bg-brand-tint-light/30 dark:active:bg-brand-tint-dark/30 transition-colors"
            onMouseDown={handleResizeStart}
          />
        )}
      </motion.div>
    </SidebarStateContext.Provider>
  );

  if (forceDark) {
    return <div className="dark shrink-0">{sidebar}</div>;
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
        border && 'border-b border-border-light dark:border-border-dark',
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
  return (
    <div
      className={cn(
        sectionPadding[padding],
        separator === 'full' && 'border-t border-border-light dark:border-border-dark',
        separator === 'padded' && 'border-t border-border-light dark:border-border-dark mx-4',
        scrollable && 'flex-1 min-h-0 overflow-y-auto',
        className,
      )}
      {...props}
    >
      {children}
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
        separator && 'border-t border-border-light dark:border-border-dark',
        className,
      )}
      {...props}
    >
      {children}
    </div>
  );
}
