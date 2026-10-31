import { type ReactNode, type HTMLAttributes } from 'react';

type BannerVariant = 'info' | 'success' | 'warning' | 'danger';

export interface BannerProps extends HTMLAttributes<HTMLDivElement> {
  variant?: BannerVariant;
  /** Optional trailing content — typically a Button. Rendered at the right edge. */
  action?: ReactNode;
  /**
   * Layout mode. The default is an edge-to-edge strip (bottom border only) for app-chrome
   * banners; `inline` renders a rounded, fully-bordered box for use inside forms and cards.
   */
  inline?: boolean;
  children: ReactNode;
}

const variantStyles: Record<BannerVariant, string> = {
  info: 'bg-info-light/15 dark:bg-info-dark/15 border-info-light/30 dark:border-info-dark/30',
  success: 'bg-success-light/15 dark:bg-success-dark/15 border-success-light/30 dark:border-success-dark/30',
  warning: 'bg-warning-light/15 dark:bg-warning-dark/15 border-warning-light/30 dark:border-warning-dark/30',
  danger: 'bg-danger-light/15 dark:bg-danger-dark/15 border-danger-light/30 dark:border-danger-dark/30',
};

export function Banner({ variant = 'info', action, inline = false, className = '', children, ...props }: BannerProps) {
  const layoutStyles = inline ? 'rounded-lg border' : 'border-b';

  return (
    <div
      className={`text-text-1-light dark:text-text-1-dark px-4 py-2 flex items-center justify-between gap-4 ${layoutStyles} ${variantStyles[variant]} ${className}`}
      {...props}
    >
      <span className="text-body-md">{children}</span>
      {action}
    </div>
  );
}
