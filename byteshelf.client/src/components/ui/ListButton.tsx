import { type ReactNode, type ButtonHTMLAttributes } from 'react';
import { Icon, type IconComponent } from './Icon';
import { cn } from '../../lib/utils';

export interface ListButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon?: IconComponent;
  iconElement?: ReactNode;
  iconSize?: number;
  iconClassName?: string;
  isActive?: boolean;
  actions?: ReactNode;
  children: ReactNode;
}

export function ListButton({
  icon,
  iconElement,
  iconSize = 16,
  iconClassName,
  isActive = false,
  actions,
  children,
  className,
  type = 'button',
  ...props
}: ListButtonProps) {
  return (
    <button
      type={type}
      data-active={isActive || undefined}
      className={cn(
        'w-full flex items-center transition-colors select-none group text-left',
        className,
      )}
      {...props}
    >
      {(icon || iconElement) && (
        <span className={cn('shrink-0 flex items-center', iconClassName)}>
          {iconElement ?? <Icon icon={icon!} size={iconSize} />}
        </span>
      )}

      {children != null && <span className="min-w-0 flex-1">{children}</span>}

      {actions}
    </button>
  );
}
