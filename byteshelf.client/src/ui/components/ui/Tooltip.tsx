import { type ReactNode, useState, useRef, useEffect, useCallback, useLayoutEffect } from 'react';
import { createPortal } from 'react-dom';

type TooltipPosition = 'top' | 'bottom' | 'left' | 'right';
type TooltipAlign = 'center' | 'start';

export interface TooltipProps {
  content: ReactNode;
  position?: TooltipPosition;
  /** Where a `left` or `right` tooltip sits along its trigger: `center` (the default) centres it
   *  vertically on the trigger; `start` sets its top edge level with the trigger's top, so a tall
   *  tooltip hangs downward only. Ignored for `top` and `bottom`. */
  align?: TooltipAlign;
  delay?: number;
  /** When true, the tooltip never shows (the wrapper element still renders so layout is unaffected). */
  disabled?: boolean;
  /** Caps the tooltip's width so long content wraps onto multiple lines instead of a single long
   *  row (a number is treated as px). Omit to keep the default single-line (`whitespace-nowrap`)
   *  behaviour. The width cap is applied before measuring, so positioning accounts for the wrap.
   *
   *  <b>It binds on content with no break opportunity too</b> — a file name, an id, a URL. The cap
   *  alone cannot: the tooltip is shrink-to-fit, so its width is at least its content's min-content
   *  width, and an unbroken token's min-content width is the whole token. `overflow-wrap: anywhere`
   *  is what makes that shrink; see the render below for why `break-words` is not the same thing. */
  maxWidth?: number | string;
  /** The element the trigger is wrapped in. `span` for a trigger standing in running text, inside a `<p>`,
   *  which may hold no `<div>`: the browser would close the paragraph early around one. */
  as?: 'div' | 'span';
  children: ReactNode;
  className?: string;
}

const GAP = 8;
const VIEWPORT_MARGIN = 8;

function getDesiredPosition(
  position: TooltipPosition,
  align: TooltipAlign,
  trigger: DOMRect,
  tooltip: DOMRect,
): { left: number; top: number } {
  const sideTop = align === 'start' ? trigger.top : trigger.top + trigger.height / 2 - tooltip.height / 2;
  switch (position) {
    case 'top':
      return {
        left: trigger.left + trigger.width / 2 - tooltip.width / 2,
        top: trigger.top - GAP - tooltip.height,
      };
    case 'bottom':
      return {
        left: trigger.left + trigger.width / 2 - tooltip.width / 2,
        top: trigger.bottom + GAP,
      };
    case 'left':
      return {
        left: trigger.left - GAP - tooltip.width,
        top: sideTop,
      };
    case 'right':
      return {
        left: trigger.right + GAP,
        top: sideTop,
      };
  }
}

function clampToViewport(
  left: number,
  top: number,
  tooltip: DOMRect,
): { left: number; top: number } {
  const maxLeft = Math.max(VIEWPORT_MARGIN, window.innerWidth - tooltip.width - VIEWPORT_MARGIN);
  const maxTop = Math.max(VIEWPORT_MARGIN, window.innerHeight - tooltip.height - VIEWPORT_MARGIN);
  return {
    left: Math.min(Math.max(left, VIEWPORT_MARGIN), maxLeft),
    top: Math.min(Math.max(top, VIEWPORT_MARGIN), maxTop),
  };
}

export function Tooltip({
  content,
  position = 'top',
  align = 'center',
  delay = 200,
  disabled = false,
  maxWidth,
  as: Trigger = 'div',
  children,
  className = '',
}: TooltipProps) {
  const [visible, setVisible] = useState(false);
  const triggerRef = useRef<HTMLDivElement & HTMLSpanElement>(null);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>(null);
  const [style, setStyle] = useState<React.CSSProperties>({ visibility: 'hidden' });

  const show = useCallback(() => {
    if (disabled) return;
    timeoutRef.current = setTimeout(() => {
      setVisible(true);
    }, delay);
  }, [delay, disabled]);

  const hide = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }
    setVisible(false);
    setStyle({ visibility: 'hidden' });
  }, []);

  // Pointer events instead of mouse events so touch taps don't spawn a tooltip that
  // sticks until the next tap elsewhere (touch has no mouseleave/pointerleave on tap).
  const handlePointerEnter = useCallback((event: React.PointerEvent<HTMLElement>) => {
    if (event.pointerType !== 'touch') show();
  }, [show]);

  // Only keyboard focus shows the tooltip — tap/click focus would stick on touch.
  const handleFocus = useCallback((event: React.FocusEvent<HTMLElement>) => {
    if (event.target.matches(':focus-visible')) show();
  }, [show]);

  // Derived so the tooltip also disappears if `disabled` flips true mid-hover.
  const isVisible = visible && !disabled;

  // Escape dismisses a visible tooltip without moving focus (WCAG 1.4.13).
  useEffect(() => {
    if (!isVisible) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') hide();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isVisible, hide]);

  useLayoutEffect(() => {
    if (!isVisible || !triggerRef.current || !tooltipRef.current) return;
    const triggerRect = triggerRef.current.getBoundingClientRect();
    const tooltipRect = tooltipRef.current.getBoundingClientRect();
    const desired = getDesiredPosition(position, align, triggerRect, tooltipRect);
    const clamped = clampToViewport(desired.left, desired.top, tooltipRect);
    setStyle({ left: clamped.left, top: clamped.top });
  }, [isVisible, position, align, content]);

  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  return (
    <Trigger
      ref={triggerRef}
      className={`relative inline-flex ${className}`}
      onPointerEnter={handlePointerEnter}
      onPointerLeave={hide}
      onFocus={handleFocus}
      onBlur={hide}
    >
      {children}
      {isVisible && createPortal(
        <div
          ref={tooltipRef}
          role="tooltip"
          style={maxWidth != null ? { ...style, maxWidth } : style}
          // `[overflow-wrap:anywhere]` rather than `break-words` (`overflow-wrap: break-word`), and
          // the difference is the whole reason the cap works. Both break a long word to fit, but
          // only `anywhere` is counted when the box's MIN-CONTENT width is computed — and this box
          // is `fixed`, so it is shrink-to-fit and sized from exactly that. With `break-word` the
          // min-content width of `Assay_Stability_Batch_20260114_final.xlsx` is still the whole
          // token, the cap is below the minimum and cannot bind, and the tooltip draws wider than
          // asked for with no wrap at all. An arbitrary property because Tailwind 3 has no utility
          // for it (v4.1's `wrap-anywhere`).
          className={`fixed z-50 px-2.5 py-1.5 text-body-sm rounded-xl pointer-events-none
            bg-panel border border-border
            text-fg shadow-brand-lg animate-fade-in ${maxWidth != null ? 'whitespace-normal [overflow-wrap:anywhere]' : 'whitespace-nowrap'}`}
        >
          {content}
        </div>,
        document.body,
      )}
    </Trigger>
  );
}
