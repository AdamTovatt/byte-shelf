import { type ReactNode, type ButtonHTMLAttributes } from 'react';
import { Spinner } from './Spinner';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'danger' | 'ghost' | 'outlinePrimary' | 'outlineDanger' | 'brand' | 'link' | 'text';
  size?: 'sm' | 'md' | 'lg' | 'xl';
  icon?: ReactNode;
  loading?: boolean;
  children?: ReactNode;
}

export function Button({
  variant = 'primary',
  size = 'md',
  icon,
  loading = false,
  disabled,
  className = '',
  children,
  ...props
}: ButtonProps) {
  const baseStyles = 'inline-flex items-center justify-center gap-2.5 font-medium transition-elegant border focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-text-1-light dark:focus-visible:ring-text-1-dark focus-visible:ring-offset-2 focus-visible:ring-offset-bg-light dark:focus-visible:ring-offset-bg-dark';

  const variantStyles = {
    primary: 'bg-elevation-2-light dark:bg-elevation-3-dark text-text-1-light dark:text-text-1-dark border-border-light dark:border-border-dark hover:bg-elevation-3-light dark:hover:bg-elevation-4-dark hover:border-elevation-4-light dark:hover:border-elevation-4-dark disabled:opacity-40 disabled:cursor-not-allowed',
    secondary: 'bg-elevation-3-light dark:bg-elevation-2-dark text-text-1-light dark:text-text-1-dark border-border-light dark:border-border-dark hover:bg-elevation-4-light dark:hover:bg-elevation-3-dark hover:border-elevation-4-light dark:hover:border-elevation-4-dark disabled:opacity-40 disabled:cursor-not-allowed',
    danger: 'bg-danger-light/10 dark:bg-danger-dark/10 text-danger-light dark:text-danger-dark border-danger-light/20 dark:border-danger-dark/20 hover:bg-danger-light/15 dark:hover:bg-danger-dark/15 hover:border-danger-light/30 dark:hover:border-danger-dark/30 disabled:opacity-40 disabled:cursor-not-allowed',
    ghost: 'bg-transparent text-text-1-light dark:text-text-1-dark border-transparent hover:bg-elevation-3-light dark:hover:bg-elevation-3-dark disabled:opacity-40 disabled:cursor-not-allowed',
    outlinePrimary: 'bg-transparent text-text-1-light dark:text-text-1-dark border border-text-1-light/40 dark:border-text-1-dark/40 hover:border-text-1-light dark:hover:border-text-1-dark hover:bg-text-1-light/5 dark:hover:bg-text-1-dark/5 disabled:opacity-40 disabled:cursor-not-allowed',
    outlineDanger: 'bg-transparent text-danger-light dark:text-danger-dark border border-danger-light/20 dark:border-danger-dark/20 hover:border-danger-light/40 dark:hover:border-danger-dark/40 hover:bg-danger-light/5 dark:hover:bg-danger-dark/5 disabled:opacity-40 disabled:cursor-not-allowed',
    brand: 'bg-elevation-2-dark dark:bg-text-1-dark text-text-1-dark dark:text-elevation-1-dark border-elevation-2-dark/10 dark:border-text-1-dark/10 hover:bg-elevation-3-dark dark:hover:bg-text-1-dark/90 disabled:opacity-40 disabled:cursor-not-allowed',
    link: 'bg-transparent text-text-1-light dark:text-text-1-dark border-transparent !p-0 !rounded-none underline disabled:opacity-40 disabled:cursor-not-allowed',
    text: 'bg-transparent text-inherit border-transparent !p-0 !rounded-none !text-[length:inherit] !leading-[inherit] !tracking-[inherit] disabled:opacity-40 disabled:cursor-not-allowed',
  };

  const sizeStyles = {
    sm: 'px-3 py-1.5 text-xs leading-[0.95rem] rounded-lg tracking-wide',
    md: 'px-4 py-2 text-sm leading-[1.1rem] rounded-lg tracking-wide',
    lg: 'px-6 py-2.5 text-base leading-[1.3rem] rounded-lg tracking-wide',
    xl: 'px-8 py-3.5 text-lg leading-[1.5rem] rounded-lg tracking-wide',
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
      ) : (
        icon && <span className="flex items-center justify-center">{icon}</span>
      )}
      {children}
    </button>
  );
}
