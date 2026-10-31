import { type InputHTMLAttributes, type ReactNode, forwardRef } from 'react';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  error?: string;
  label?: string;
  labelClassName?: string;
  icon?: ReactNode;
  endElement?: ReactNode;
  variant?: 'default' | 'flat' | 'outlined';
}

const wrapperVariants = {
  default: `
    rounded-lg
    bg-elevation-3-light dark:bg-elevation-3-dark
    border border-elevation-5-light dark:border-border-dark
    focus-within:border-text-5-light/50 dark:focus-within:border-text-5-dark/50
    focus-within:shadow-focus-glow
  `,
  flat: `
    rounded-lg
    bg-elevation-2-light dark:bg-elevation-2-dark
    border border-border-light dark:border-border-dark
    focus-within:border-text-5-light/50 dark:focus-within:border-text-5-dark/50
  `,
  outlined: `
    rounded-lg
    bg-elevation-2-light dark:bg-elevation-2-dark
    border border-border-light dark:border-border-dark
    focus-within:border-text-5-light/50 dark:focus-within:border-text-5-dark/50
  `,
};

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ error, label, labelClassName, icon, endElement, variant = 'default', className = '', id, ...props }, ref) => {
    const inputId = id || label?.toLowerCase().replace(/\s+/g, '-');

    const errorBorder = error
      ? 'border-danger-light dark:border-danger-dark focus-within:border-danger-light dark:focus-within:border-danger-dark'
      : '';

    return (
      <div className="flex flex-col gap-2 w-full">
        {label && (
          <label
            htmlFor={inputId}
            className={labelClassName || "text-body-lg font-medium text-text-1-light dark:text-text-1-dark"}
          >
            {label}
          </label>
        )}
        <div
          className={`
            relative
            ${wrapperVariants[variant]}
            transition-shadow duration-200
            ${errorBorder}
          `}
        >
          {icon && (
            <div className="absolute left-4 top-1/2 -translate-y-1/2 z-10 text-text-3-light dark:text-text-3-dark pointer-events-none">
              {icon}
            </div>
          )}
          <input
            ref={ref}
            id={inputId}
            className={`
              w-full px-3 py-2 rounded-lg
              bg-transparent text-text-1-light dark:text-text-1-dark
              placeholder:text-text-5-light dark:placeholder:text-text-5-dark
              outline-none border-none
              disabled:opacity-40 disabled:cursor-not-allowed
              ${icon ? 'pl-12' : ''}
              ${endElement ? 'pr-12' : ''}
              ${className}
            `}
            {...props}
          />
          {endElement && (
            <div className="absolute right-2 top-1/2 -translate-y-1/2 z-10">
              {endElement}
            </div>
          )}
        </div>
        {error && (
          <span className="text-body-lg text-danger-light dark:text-danger-dark">{error}</span>
        )}
      </div>
    );
  }
);

Input.displayName = 'Input';
