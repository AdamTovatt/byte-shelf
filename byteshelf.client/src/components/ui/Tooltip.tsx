import { type ReactNode, useState, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';

type TooltipPosition = 'top' | 'bottom' | 'left' | 'right';

export interface TooltipProps {
  content: ReactNode;
  position?: TooltipPosition;
  delay?: number;
  children: ReactNode;
  className?: string;
  /**
   * When true, the tooltip wraps long content instead of staying on a single
   * line. Useful for sentence-length summaries.
   */
  wrap?: boolean;
}

const GAP = 8;

function getTooltipStyle(
  position: TooltipPosition,
  rect: DOMRect,
): React.CSSProperties {
  switch (position) {
    case 'top':
      return { left: rect.left + rect.width / 2, top: rect.top - GAP, transform: 'translate(-50%, -100%)' };
    case 'bottom':
      return { left: rect.left + rect.width / 2, top: rect.bottom + GAP, transform: 'translate(-50%, 0)' };
    case 'left':
      return { left: rect.left - GAP, top: rect.top + rect.height / 2, transform: 'translate(-100%, -50%)' };
    case 'right':
      return { left: rect.right + GAP, top: rect.top + rect.height / 2, transform: 'translate(0, -50%)' };
  }
}

export function Tooltip({
  content,
  position = 'top',
  delay = 200,
  children,
  className = '',
  wrap = false,
}: TooltipProps) {
  const [visible, setVisible] = useState(false);
  const triggerRef = useRef<HTMLDivElement>(null);
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>(null);
  const [style, setStyle] = useState<React.CSSProperties>({});

  const show = useCallback(() => {
    timeoutRef.current = setTimeout(() => {
      if (triggerRef.current) {
        const rect = triggerRef.current.getBoundingClientRect();
        setStyle(getTooltipStyle(position, rect));
      }
      setVisible(true);
    }, delay);
  }, [delay, position]);

  const hide = useCallback(() => {
    if (timeoutRef.current) {
      clearTimeout(timeoutRef.current);
    }
    setVisible(false);
  }, []);

  useEffect(() => {
    return () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  return (
    <div
      ref={triggerRef}
      className={`relative inline-flex ${className}`}
      onMouseEnter={show}
      onMouseLeave={hide}
      onFocus={show}
      onBlur={hide}
    >
      {children}
      {visible && createPortal(
        <div
          role="tooltip"
          style={style}
          className={`fixed z-50 px-2.5 py-1.5 text-body-md rounded-xl pointer-events-none
            bg-elevation-2-light dark:bg-elevation-2-dark border border-border-light dark:border-border-dark
            text-text-1-light dark:text-text-1-dark shadow-brand-lg animate-fade-in
            ${wrap ? 'whitespace-normal break-words max-w-xs' : 'whitespace-nowrap'}`}
        >
          {content}
        </div>,
        document.body,
      )}
    </div>
  );
}
