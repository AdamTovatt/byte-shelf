import { type HTMLAttributes } from 'react';

export const DIVIDER_CARD_INSET = 3;

export interface DividerProps extends HTMLAttributes<HTMLDivElement> {
  orientation?: 'horizontal' | 'vertical';
  variant?: 'default' | 'thick';
  /**
   * Adds symmetric padding around the separator line.
   * The value is multiplied by 4 to get the pixel amount (e.g. inset={3} = 12px).
   *
   * When set, an outer wrapper `<div>` is rendered to hold the padding and `className`
   * is applied to that wrapper. When unset, `className` is applied directly to the
   * separator element.
   */
  inset?: number;
}

export function Divider({
  orientation = 'horizontal',
  variant = 'default',
  inset,
  className = '',
  ...props
}: DividerProps) {
  const isThick = variant === 'thick';

  const orientationStyles = orientation === 'horizontal'
    ? `w-full ${isThick ? 'h-0.5' : 'h-px'}`
    : `h-full ${isThick ? 'w-0.5' : 'w-px'}`;

  const colorStyles = isThick
    ? 'divider-thick rounded-full'
    : 'bg-border-light dark:bg-border-dark';

  const line = (
    <div
      role="separator"
      aria-orientation={orientation}
      className={`${orientationStyles} ${colorStyles} shrink-0 ${inset == null ? className : ''}`}
      {...props}
    />
  );

  if (inset == null) return line;

  const paddingStyle = orientation === 'horizontal'
    ? { paddingLeft: `${inset * 4}px`, paddingRight: `${inset * 4}px` }
    : { paddingTop: `${inset * 4}px`, paddingBottom: `${inset * 4}px` };

  return (
    <div className={className} style={paddingStyle}>
      {line}
    </div>
  );
}
