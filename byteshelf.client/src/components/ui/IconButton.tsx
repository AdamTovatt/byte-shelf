import { type ReactNode, type ButtonHTMLAttributes } from 'react';
import { Icon, type IconComponent } from './Icon';
import { Spinner } from './Spinner';

const iconSizeMap = {
  sm: 16,
  md: 20,
  lg: 24,
} as const;

export interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'default' | 'primary' | 'secondary' | 'danger' | 'ghost';
  size?: 'sm' | 'md' | 'lg';
  icon?: IconComponent;
  iconStrokeWidth?: number;
  iconClassName?: string;
  loading?: boolean;
  children?: ReactNode;
}

export function IconButton({
  variant = 'default',
  size = 'md',
  className = '',
  icon,
  iconStrokeWidth,
  iconClassName,
  children,
  disabled,
  loading = false,
  ...props
}: IconButtonProps) {
  const baseStyles = 'inline-flex items-center justify-center rounded-lg transition-all';

  const variantStyles = {
    default: 'bg-transparent text-text-4-light dark:text-text-4-dark hover:text-text-1-light dark:hover:text-text-1-dark hover:bg-elevation-4-light dark:hover:bg-elevation-4-dark disabled:cursor-not-allowed disabled:opacity-40',
    primary: 'bg-elevation-2-light dark:bg-elevation-3-dark border border-border-light dark:border-border-dark text-text-1-light dark:text-text-1-dark hover:bg-elevation-3-light dark:hover:bg-elevation-4-dark hover:border-elevation-4-light dark:hover:border-elevation-4-dark disabled:cursor-not-allowed disabled:opacity-40',
    secondary: 'bg-elevation-3-light dark:bg-elevation-2-dark border border-border-light dark:border-border-dark text-text-1-light dark:text-text-1-dark hover:bg-elevation-4-light dark:hover:bg-elevation-3-dark hover:border-elevation-4-light dark:hover:border-elevation-4-dark disabled:cursor-not-allowed disabled:opacity-40',
    danger: 'bg-transparent text-text-4-light dark:text-text-4-dark hover:text-danger-light dark:hover:text-danger-dark hover:bg-danger-light/10 dark:hover:bg-danger-dark/10 disabled:cursor-not-allowed disabled:opacity-40',
    ghost: 'bg-transparent text-text-4-light dark:text-text-4-dark hover:text-text-1-light dark:hover:text-text-1-dark hover:bg-elevation-3-light dark:hover:bg-elevation-3-dark disabled:cursor-not-allowed disabled:opacity-40',
  };

  const sizeStyles = {
    sm: 'p-1 text-sm',
    md: 'p-2 text-base',
    lg: 'p-3 text-lg',
  };

  const combinedClassName = `${baseStyles} ${variantStyles[variant]} ${sizeStyles[size]} ${className}`;

  return (
    <button
      className={combinedClassName}
      disabled={disabled || loading}
      {...props}
    >
      {loading ? (
        <Spinner size="sm" />
      ) : icon ? (
        <Icon icon={icon} size={iconSizeMap[size]} strokeWidth={iconStrokeWidth} className={iconClassName} />
      ) : (
        children
      )}
    </button>
  );
}
