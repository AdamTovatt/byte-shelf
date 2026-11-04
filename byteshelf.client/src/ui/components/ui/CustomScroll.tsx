import type { ReactNode, UIEvent } from 'react';
import { cn } from '../../lib/utils';
import { useVerticalScrollOverlay } from '../../hooks/useVerticalScrollOverlay';
import { overlayHidePendingAttributes } from '../../hooks/scrollOverlay';
import { ScrollThumb } from './ScrollThumb';

export interface CustomScrollProps {
  children: ReactNode;
  className?: string;
  flex?: string;
  heightRelativeToParent?: string;
  onScroll?: (event: UIEvent) => void;
  /** Offset the scrollbar track from the top (e.g. to clear a sticky header) */
  scrollBarTopOffset?: number;
  /** Exposes the inner scrollable container element */
  scrollContainerRef?: React.RefObject<HTMLDivElement | null>;
  /**
   * Classes for the content wrapper — the element that carries the `minHeight: 100%` a `flex` /
   * `heightRelativeToParent` scroller gives it, NOT the scroller and not the children.
   *
   * It exists for one thing `className` cannot do: let content be laid out against the pane's full
   * height. A child asking for that with `min-h-full` gets a percentage against this wrapper's own
   * `auto` height and collapses to zero; `contentClassName="flex flex-col"` plus `flex-1` on the
   * child resolves through flexbox instead, which reads the wrapper's used height with its floor
   * applied. That is what lets a short body sit centred in the pane and a long one still scroll.
   */
  contentClassName?: string;
}

export function CustomScroll({
  children,
  className = '',
  flex,
  heightRelativeToParent,
  onScroll,
  scrollBarTopOffset = 0,
  scrollContainerRef,
  contentClassName,
}: CustomScrollProps) {
  const {
    setScrollContainer,
    contentRef,
    trackRef,
    hasScroll,
    isVisible,
    isHidePending,
    handleTop,
    handleHeight,
    onScroll: handleScroll,
    onMouseEnter,
    onMouseLeave,
    onTrackClick,
    onHandleMouseDown,
  } = useVerticalScrollOverlay({ scrollBarTopOffset, onScroll, scrollContainerRef });

  const outerStyle: React.CSSProperties = {};
  if (heightRelativeToParent) {
    outerStyle.height = heightRelativeToParent;
  }
  if (flex) {
    outerStyle.flex = flex;
  }

  const contentStyle: React.CSSProperties = {
    minHeight: heightRelativeToParent || flex ? '100%' : undefined,
  };

  return (
    <div
      className={cn('custom-scroll', className)}
      style={outerStyle}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
    >
      <div className="custom-scroll-outer">
        {hasScroll && (
          <div className="custom-scroll-track-wrapper">
            <div
              ref={trackRef}
              className={cn('custom-scroll-track', isVisible && 'custom-scroll-track-visible')}
              {...overlayHidePendingAttributes(isHidePending)}
              style={scrollBarTopOffset ? { top: scrollBarTopOffset } : undefined}
              onClick={onTrackClick}
            >
              <div
                className="custom-scroll-handle"
                style={{ top: handleTop, height: handleHeight }}
                onMouseDown={onHandleMouseDown}
              >
                <ScrollThumb />
              </div>
            </div>
          </div>
        )}
        <div
          ref={setScrollContainer}
          className="custom-scroll-inner"
          onScroll={handleScroll}
        >
          <div ref={contentRef} className={contentClassName} style={contentStyle}>
            {children}
          </div>
        </div>
      </div>
    </div>
  );
}
