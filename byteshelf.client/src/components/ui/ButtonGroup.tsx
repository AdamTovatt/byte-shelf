import { type ReactNode, Fragment, useRef, useState, useEffect, useCallback } from 'react';
import { motion } from 'motion/react';
import { Tooltip } from './Tooltip';
import { springs } from '../../lib/springs';

type ButtonGroupSize = 'sm' | 'md';

interface ButtonGroupOption<T extends string> {
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
      className={`relative flex gap-1 p-1 bg-elevation-2-light dark:bg-elevation-2-dark border border-border-light dark:border-border-dark ${isVertical ? 'flex-col rounded-lg' : 'rounded-lg'} ${className}`}
      {...(ariaTablist ? { role: 'tablist' } : {})}
    >
      {indicatorStyle && (
        <motion.div
          initial={false}
          animate={indicatorStyle}
          transition={hasInteracted ? springs.bouncy : { duration: 0 }}
          className={`absolute bg-elevation-3-light dark:bg-elevation-3-dark border border-border-light dark:border-border-dark shadow-sm ${isVertical ? 'left-1 right-1 rounded-md' : 'top-1 bottom-1 rounded-md'}`}
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
            onClick={() => { setHasInteracted(true); onChange(option.value); }}
            {...(ariaTablist ? { role: 'tab', 'aria-selected': isActive } : {})}
            className={`
              relative z-[1] flex flex-1 items-center justify-center rounded-md font-medium transition-colors duration-200
              ${iconOnly ? iconOnlySizeStyles[size] : sizeStyles[size]}
              ${isActive
                ? 'text-text-1-light dark:text-text-1-dark'
                : 'text-text-3-light dark:text-text-3-dark'
              }
            `}
          >
            {option.icon && (
              <span className={isActive ? 'text-text-1-light dark:text-text-1-dark' : 'text-text-3-light dark:text-text-3-dark'}>
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
