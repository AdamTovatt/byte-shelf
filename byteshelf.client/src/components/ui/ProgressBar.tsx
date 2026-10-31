import { cn } from '../../lib/utils';

export interface ProgressBarProps {
  /** How far along, 0 to 1. */
  value: number;
  variant?: 'brand' | 'warning' | 'danger';
  label?: string;
  className?: string;
}

const fillStyles: Record<NonNullable<ProgressBarProps['variant']>, string> = {
  brand: 'bg-brand-primary-light dark:bg-brand-primary-dark',
  warning: 'bg-warning-light dark:bg-warning-dark',
  danger: 'bg-danger-light dark:bg-danger-dark',
};

/** A thin bar showing how much of something is done or used. */
export function ProgressBar({ value, variant = 'brand', label, className }: ProgressBarProps) {
  const percent = Math.round(Math.min(Math.max(value, 0), 1) * 100);
  return (
    <div
      role="progressbar"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={percent}
      aria-label={label}
      className={cn('h-1.5 w-full overflow-hidden rounded-full bg-elevation-3-light dark:bg-elevation-3-dark', className)}
    >
      <div className={cn('h-full rounded-full transition-[width] duration-300', fillStyles[variant])} style={{ width: `${percent}%` }} />
    </div>
  );
}
