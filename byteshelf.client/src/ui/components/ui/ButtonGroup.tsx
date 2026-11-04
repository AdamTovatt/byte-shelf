import { type ReactNode, Fragment, useRef, useState, useEffect, useCallback } from 'react';
import { motion } from 'motion/react';
import { Tooltip } from './Tooltip';
import { springs } from '../../lib/springs';

type ButtonGroupSize = 'sm' | 'md';

export interface ButtonGroupOption<T extends string> {
  value: T;
  label?: string;
  icon?: ReactNode;
  tooltip?: string;
}

export interface ButtonGroupProps<T extends string> {
  options: ButtonGroupOption<T>[];
  value: T;
  onChange: (value: T) => void;
  size?: ButtonGroupSize;
  orientation?: 'horizontal' | 'vertical';
  className?: string;
  /** When true, adds role="tablist" to the container and role="tab" + aria-selected to each button. */
  ariaTablist?: boolean;
  /** Names the group for a screen reader, where nothing on screen labels it. */
  ariaLabel?: string;
  /** Holds the current choice: every option is disabled, and a click changes nothing. */
  disabled?: boolean;
  /** The pointer entered an option: what choosing it would show can be read ahead, before the click. */
  onOptionHover?: (value: T) => void;
}

const sizeStyles: Record<ButtonGroupSize, string> = {
  sm: 'px-3 py-1.5 text-xs gap-1.5',
  md: 'px-4 py-2 text-sm gap-2',
};

const iconOnlySizeStyles: Record<ButtonGroupSize, string> = {
  sm: 'p-1.5',
  md: 'p-2',
};

export function ButtonGroup<T extends string>({
  options,
  value,
  onChange,
  size = 'md',
  orientation = 'horizontal',
  className = '',
  ariaTablist = false,
  ariaLabel,
  disabled = false,
  onOptionHover,
}: ButtonGroupProps<T>) {
  const isVertical = orientation === 'vertical';
  const containerRef = useRef<HTMLDivElement>(null);
  const buttonRefs = useRef<Map<string, HTMLButtonElement>>(new Map());
  const [indicatorStyle, setIndicatorStyle] = useState<{ top?: number; left?: number; width?: number; height?: number } | null>(null);
  const [hasInteracted, setHasInteracted] = useState(false);

  const updateIndicator = useCallback(() => {
    const container = containerRef.current;
    const activeButton = buttonRefs.current.get(value);
    if (!container || !activeButton) return;

    const containerRect = container.getBoundingClientRect();
    const buttonRect = activeButton.getBoundingClientRect();

    if (containerRect.width === 0 || buttonRect.width === 0) return;

    // When inside a scaled parent (e.g. animating popover), getBoundingClientRect
    // returns visual (post-transform) sizes. Detect the scale factor by comparing
    // the container's reported width to its layout width.
    const scale = containerRect.width / container.offsetWidth;
    if (scale === 0) return;

    // Subtract the container's border (clientLeft/clientTop) since the indicator
    // is positioned relative to the padding box, not the border box.
    const borderLeft = container.clientLeft * scale;
    const borderTop = container.clientTop * scale;

    if (isVertical) {
      setIndicatorStyle({
        top: (buttonRect.top - containerRect.top - borderTop) / scale,
        height: buttonRect.height / scale,
      });
    } else {
      setIndicatorStyle({
        left: (buttonRect.left - containerRect.left - borderLeft) / scale,
        width: buttonRect.width / scale,
      });
    }
  }, [value, isVertical]);

  useEffect(() => {
    updateIndicator();
    window.addEventListener('resize', updateIndicator);

    // Re-measure when the container becomes visible (e.g. inside a dropdown/popover
    // that animates in — getBoundingClientRect returns zeros while hidden)
    const container = containerRef.current;
    let observer: ResizeObserver | undefined;
    if (container) {
      observer = new ResizeObserver(() => updateIndicator());
      observer.observe(container);
    }

    return () => {
      window.removeEventListener('resize', updateIndicator);
      observer?.disconnect();
    };
  }, [updateIndicator]);

  return (
    <div
      ref={containerRef}
      className={`relative flex gap-1 p-1 bg-panel border border-border ${isVertical ? 'flex-col rounded-lg' : 'rounded-lg'} ${className}`}
      {...(ariaTablist ? { role: 'tablist' } : {})}
      {...(ariaLabel ? { 'aria-label': ariaLabel } : {})}
    >
      {indicatorStyle && (
        <motion.div
          initial={false}
          animate={indicatorStyle}
          transition={hasInteracted ? springs.bouncy : { duration: 0 }}
          className={`absolute bg-surface border border-border shadow-sm ${isVertical ? 'left-1 right-1 rounded-md' : 'top-1 bottom-1 rounded-md'}`}
        />
      )}
      {options.map((option) => {
        const isActive = option.value === value;
        const iconOnly = option.icon && !option.label;

        const button = (
          <button
            ref={(el) => {
              if (el) buttonRefs.current.set(option.value, el);
            }}
            type="button"
            disabled={disabled}
            onClick={() => { setHasInteracted(true); onChange(option.value); }}
            onMouseEnter={onOptionHover ? () => onOptionHover(option.value) : undefined}
            // An icon-only option has no text to be named by, and `Tooltip` names only its own
            // bubble — it sets `role="tooltip"` on the floating element and puts nothing on the
            // child. So every icon-only option in the product rendered as a bare "button" to a
            // screen reader: the two theme pickers, the report view switch, and any that follow.
            // The tooltip is already the word for the option, so it is what the name is taken from
            // rather than a second prop each call site could forget. Labelled options need nothing —
            // their text is their name, and an `aria-label` there would override it.
            {...(iconOnly && option.tooltip ? { 'aria-label': option.tooltip } : {})}
            {...(ariaTablist ? { role: 'tab', 'aria-selected': isActive } : {})}
            className={`
              relative z-[1] flex flex-1 items-center justify-center whitespace-nowrap rounded-md font-medium transition-colors duration-200
              disabled:cursor-not-allowed disabled:opacity-50
              ${iconOnly ? iconOnlySizeStyles[size] : sizeStyles[size]}
              ${isActive
                ? 'text-fg'
                : 'text-muted'
              }
            `}
          >
            {option.icon && (
              <span className={isActive ? 'text-fg' : 'text-muted'}>
                {option.icon}
              </span>
            )}
            {option.label && (
              <span className={option.icon ? 'hidden sm:inline' : ''}>{option.label}</span>
            )}
          </button>
        );

        if (option.tooltip) {
          return (
            <Tooltip key={option.value} content={option.tooltip} position={isVertical ? 'right' : 'top'} className="flex-1">
              {button}
            </Tooltip>
          );
        }

        return <Fragment key={option.value}>{button}</Fragment>;
      })}
    </div>
  );
}
