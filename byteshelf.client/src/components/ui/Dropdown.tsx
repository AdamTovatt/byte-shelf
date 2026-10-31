import { type ReactNode, useState, useCallback, createContext, useContext } from 'react';
import { Popover } from './Popover';

const DropdownCloseContext = createContext<(() => void) | null>(null);

type DropdownAlign = 'left' | 'right' | 'auto';
type DropdownPosition = 'below' | 'above' | 'auto';

export interface DropdownProps {
  trigger: ReactNode;
  align?: DropdownAlign;
  position?: DropdownPosition;
  children: ReactNode;
  className?: string;
  onOpenChange?: (open: boolean) => void;
}

export function Dropdown({
  trigger,
  align = 'auto',
  position = 'auto',
  children,
  className = '',
  onOpenChange,
}: DropdownProps) {
  const [open, setOpen] = useState(false);

  const handleOpenChange = useCallback((next: boolean) => {
    setOpen(next);
    onOpenChange?.(next);
  }, [onOpenChange]);

  const close = useCallback(() => handleOpenChange(false), [handleOpenChange]);

  return (
    <Popover
      trigger={trigger}
      align={align}
      position={position}
      className={className}
      open={open}
      onOpenChange={handleOpenChange}
      panelClassName="min-w-[180px] overflow-hidden py-1.5"
      triggerClassName="w-full"
    >
      <DropdownCloseContext.Provider value={close}>
        {children}
      </DropdownCloseContext.Provider>
    </Popover>
  );
}

export interface DropdownItemProps {
  icon?: ReactNode;
  variant?: 'default' | 'danger';
  disabled?: boolean;
  onClick?: () => void;
  children: ReactNode;
}

export function DropdownItem({
  icon,
  variant = 'default',
  disabled = false,
  onClick,
  children,
}: DropdownItemProps) {
  const closeDropdown = useContext(DropdownCloseContext);

  const handleClick = () => {
    onClick?.();
    closeDropdown?.();
  };

  const variantStyles = {
    default: 'text-text-1-light dark:text-text-1-dark hover:bg-elevation-4-light dark:hover:bg-elevation-3-dark',
    danger: 'text-danger-light dark:text-danger-dark hover:bg-danger-light/10 dark:hover:bg-danger-dark/10',
  };

  const iconStyles = {
    default: 'text-text-4-light dark:text-text-4-dark',
    danger: 'text-danger-light dark:text-danger-dark',
  };

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={handleClick}
      className={`w-full text-left flex items-center gap-2.5 px-3.5 py-2 text-body-lg transition-colors
        ${variantStyles[variant]}
        disabled:opacity-40 disabled:cursor-not-allowed`}
    >
      {icon && <span className={`flex items-center shrink-0 [&_svg]:stroke-current ${iconStyles[variant]}`}>{icon}</span>}
      {children}
    </button>
  );
}
