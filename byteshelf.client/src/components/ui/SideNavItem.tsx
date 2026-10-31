import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion } from 'motion/react';
import { iconMap } from '../../lib/iconMap';
import { Icon, type IconComponent } from './Icon';
import { IconButton } from './IconButton';
import { ListButton } from './ListButton';
import { Tooltip } from './Tooltip';
import { cn } from '../../lib/utils';
import { springs } from '../../lib/springs';

export interface SideNavItemProps {
  label: string;
  icon?: IconComponent;
  iconElement?: ReactNode;
  href?: string;
  isActive?: boolean;
  depth?: number;
  hasChildren?: boolean;
  isExpanded?: boolean;
  onToggle?: () => void;
  onClick?: () => void;
  onMouseEnter?: () => void;
  isCollapsed?: boolean;
  actions?: ReactNode;
  className?: string;
}

const depthPadding: Record<number, string> = {
  0: 'pl-3',
  1: 'pl-8',
  2: 'pl-12',
};

export function SideNavItem({
  label,
  icon,
  iconElement,
  href,
  isActive = false,
  depth = 0,
  hasChildren = false,
  isExpanded = false,
  onToggle,
  onClick,
  onMouseEnter,
  isCollapsed = false,
  actions,
  className,
}: SideNavItemProps) {
  const navigate = useNavigate();

  const handleClick = () => {
    if (onClick) {
      onClick();
    } else if (hasChildren && onToggle) {
      onToggle();
    } else if (href) {
      navigate(href);
    }
  };

  const padding = depthPadding[depth] ?? 'pl-3';

  const content = (
    <ListButton
      icon={icon}
      iconElement={iconElement}
      iconSize={18}
      isActive={isActive}
      onClick={handleClick}
      onMouseEnter={onMouseEnter}
      className={cn(
        'gap-2 py-2 pr-3 rounded-lg cursor-pointer mb-px text-body-lg font-medium duration-200',
        padding,
        isCollapsed && 'justify-center px-0 pl-0',
        isActive
          ? 'bg-elevation-4-light dark:bg-elevation-3-dark text-text-1-light dark:text-text-1-dark'
          : 'text-text-3-light dark:text-text-3-dark hover:text-text-1-light dark:hover:text-text-1-dark hover:bg-elevation-4-light dark:hover:bg-elevation-3-dark',
        className,
      )}
      aria-label={isCollapsed ? label : undefined}
      actions={!isCollapsed ? (
        <>
          {actions}
          {hasChildren && (
            <IconButton
              variant="ghost"
              size="sm"
              onClick={(e) => {
                e.stopPropagation();
                onToggle?.();
              }}
              aria-label={isExpanded ? `Collapse ${label}` : `Expand ${label}`}
            >
              <motion.span
                animate={{ rotate: isExpanded ? 90 : 0 }}
                transition={springs.snappy}
                className="flex items-center"
              >
                <Icon icon={iconMap.chevronRight} size={14} />
              </motion.span>
            </IconButton>
          )}
        </>
      ) : undefined}
    >
      {!isCollapsed ? (
        <span className="truncate">{label}</span>
      ) : null}
    </ListButton>
  );

  if (isCollapsed && (icon || iconElement)) {
    return (
      <Tooltip content={label} position="right" delay={100} className="w-full">
        {content}
      </Tooltip>
    );
  }

  return content;
}
