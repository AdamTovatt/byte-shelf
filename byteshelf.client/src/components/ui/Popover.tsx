import { type ReactNode, useState, useRef, useEffect, useLayoutEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { AnimatePresence, motion } from 'motion/react';
import { springs } from '../../lib/springs';

type PopoverAlign = 'left' | 'right' | 'auto';
type PopoverPosition = 'below' | 'above' | 'auto';

type ResolvedAlign = Exclude<PopoverAlign, 'auto'>;
type ResolvedPosition = Exclude<PopoverPosition, 'auto'>;

export interface PopoverProps {
  trigger: ReactNode;
  align?: PopoverAlign;
  position?: PopoverPosition;
  children: ReactNode;
  className?: string;
  /** Controlled open state. When provided, the component is controlled. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Extra classes applied to the floating panel element. */
  panelClassName?: string;
  /** Extra classes applied to the trigger wrapper. */
  triggerClassName?: string;
}

export function Popover({
  trigger,
  align = 'auto',
  position = 'auto',
  children,
  className = '',
  open: controlledOpen,
  onOpenChange,
  panelClassName = '',
  triggerClassName = '',
}: PopoverProps) {
  const isControlled = controlledOpen !== undefined;
  const [uncontrolledOpen, setUncontrolledOpen] = useState(false);
  const open = isControlled ? controlledOpen : uncontrolledOpen;

  const setOpen = useCallback((next: boolean) => {
    if (!isControlled) setUncontrolledOpen(next);
    onOpenChange?.(next);
  }, [isControlled, onOpenChange]);

  const triggerRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  const [resolvedAlign, setResolvedAlign] = useState<ResolvedAlign>(align === 'auto' ? 'left' : align);
  const [resolvedPosition, setResolvedPosition] = useState<ResolvedPosition>(position === 'auto' ? 'below' : position);

  const close = useCallback(() => setOpen(false), [setOpen]);

  // Position the panel once it mounts and we can measure it. When align or
  // position is 'auto', choose the direction that points the panel toward the
  // viewport's center — so a trigger near the bottom opens upward, a trigger
  // near the right edge opens leftward, etc.
  useLayoutEffect(() => {
    if (!open || !panelRef.current || !triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();

    const nextAlign: ResolvedAlign = align === 'auto'
      ? (rect.left + rect.width / 2 > window.innerWidth / 2 ? 'right' : 'left')
      : align;
    const nextPosition: ResolvedPosition = position === 'auto'
      ? (rect.top + rect.height / 2 > window.innerHeight / 2 ? 'above' : 'below')
      : position;

    if (nextAlign !== resolvedAlign) setResolvedAlign(nextAlign);
    if (nextPosition !== resolvedPosition) setResolvedPosition(nextPosition);

    // Cap the panel width to the viewport (minus an edge margin) so a panel that's
    // wider than the screen — common on narrow phones — shrinks instead of clipping
    // off-edge. Setting maxWidth here forces a reflow before we read offsetWidth, so
    // the measurement reflects the post-cap width.
    const EDGE_MARGIN = 8;
    panelRef.current.style.maxWidth = `${window.innerWidth - EDGE_MARGIN * 2}px`;

    const panelWidth = panelRef.current.offsetWidth;
    const panelHeight = panelRef.current.offsetHeight;

    // Compute the desired left edge based on alignment, then clamp into the viewport
    // so the panel never sticks out beyond either edge.
    const desiredLeft = nextAlign === 'right' ? rect.right - panelWidth : rect.left;
    const maxLeft = window.innerWidth - panelWidth - EDGE_MARGIN;
    const clampedLeft = Math.max(EDGE_MARGIN, Math.min(desiredLeft, maxLeft));

    panelRef.current.style.left = `${clampedLeft}px`;
    panelRef.current.style.top = `${nextPosition === 'above' ? rect.top - panelHeight - 8 : rect.bottom + 6}px`;
    panelRef.current.style.transformOrigin = `${nextAlign === 'right' ? 'right' : 'left'} ${nextPosition === 'above' ? 'bottom' : 'top'}`;
    panelRef.current.style.visibility = 'visible';
  }, [open, align, position, resolvedAlign, resolvedPosition]);

  useEffect(() => {
    if (!open) return;

    function handleClickOutside(event: MouseEvent) {
      const target = event.target as Node;
      if (
        triggerRef.current && !triggerRef.current.contains(target) &&
        !(event.target as HTMLElement).closest('[data-popover-panel]')
      ) {
        close();
      }
    }

    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        close();
      }
    }

    function handleScroll(event: Event) {
      const target = event.target as HTMLElement;
      if (target?.closest?.('[data-popover-panel]')) return;
      close();
    }

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleEscape);
    window.addEventListener('scroll', handleScroll, true);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleEscape);
      window.removeEventListener('scroll', handleScroll, true);
    };
  }, [open, close]);

  return (
    <div ref={triggerRef} className={`inline-flex ${className}`}>
      <div onClick={() => setOpen(!open)} className={`cursor-pointer ${triggerClassName}`}>
        {trigger}
      </div>
      {createPortal(
        <AnimatePresence>
          {open && (
            <motion.div
              ref={panelRef}
              data-popover-panel
              initial={{ opacity: 0, scale: 0.4 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.4, transition: { duration: 0.15 } }}
              transition={springs.snappy}
              style={{
                position: 'fixed',
                top: -9999,
                left: -9999,
                transformOrigin: `${resolvedAlign === 'right' ? 'right' : 'left'} ${resolvedPosition === 'above' ? 'bottom' : 'top'}`,
                visibility: 'hidden',
              }}
              className={`z-50
                bg-elevation-2-light dark:bg-elevation-2-dark
                border border-border-light dark:border-border-dark
                rounded-xl shadow-brand-lg ${panelClassName}`}
            >
              {children}
            </motion.div>
          )}
        </AnimatePresence>,
        document.body,
      )}
    </div>
  );
}
