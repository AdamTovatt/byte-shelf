import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { motion } from 'motion/react';
import { iconMap } from '../../lib/iconMap';
import { Icon, type IconComponent } from './Icon';
import { IconButton } from './IconButton';
import { ListButton } from './ListButton';
import { SIDE_NAV_ICON_SIZE } from './sideNavIconSize';
import { SIDE_NAV_ROW_HOVER } from './sideNavRowHover';
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
  /**
   * Extra words said after the row's name, e.g. a count a badge beside it draws. A DESCRIPTION rather
   * than part of the name, so the name stays the visible label a speech user says to press it.
   */
  'aria-describedby'?: string;
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
  'aria-describedby': ariaDescribedBy,
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
  const horizontalLayout = isCollapsed ? 'justify-center px-0' : cn('pr-3', padding);

  const content = (
    <ListButton
      icon={icon}
      iconElement={iconElement}
      iconSize={SIDE_NAV_ICON_SIZE}
      // The row itself drives the colour (muted → fg on hover/active, below), and the icon follows it.
      iconTone="inherit"
      isActive={isActive}
      onClick={handleClick}
      onMouseEnter={onMouseEnter}
      className={cn(
        'gap-2 py-2 rounded-lg cursor-pointer mb-px text-body-md font-medium duration-200',
        horizontalLayout,
        // Hover and selection share one fill, `surface` — the fill a card wears — as `ChatSessionList`'s
        // rows do. Hover also brightens the ink to `text-fg`, so while the pointer is on an inactive row
        // it looks exactly like the selected one, as a chat row under the pointer looks like the open
        // chat; on a project row only the open-folder icon (`iconMap.projectOpen`) still tells them
        // apart. That is the decision, not an oversight: at rest the selected row is the only filled,
        // `text-fg` row in the rail. Hover used to be `surface-hover`, a step DARKER than the selected
        // row, which made the row you were pointing at outrank the one you were on.
        // `SIDE_NAV_ROW_HOVER` is that fill, shared with the rail's other rows.
        //
        // The bare alias, NOT a per-theme token: one that is `surface` in light and `panel` in dark
        // resolves, on a rail painted `panel`, to the rail itself — so in all three dark themes a
        // selected row would resolve to its own background and vanish. `surface` is `neutral-3` in
        // every theme, clear of the rail either way. `colors.test.js` holds that distance.
        isActive
          ? 'bg-surface text-fg'
          : cn('text-muted hover:text-fg', SIDE_NAV_ROW_HOVER),
        className,
      )}
      aria-label={isCollapsed ? label : undefined}
      aria-describedby={ariaDescribedBy}
      // The actions sit in a strip pulled in by `-my-1`, so a `size="sm"` IconButton (24px) takes 16px
      // of layout height — less than the 20px line — and the row is 36px whether or not they show. A
      // row whose hover actions are hidden at rest (a project row) therefore cannot step down and up as
      // the pointer travels the list, and every row is the destination rows' height. The strip passes
      // clicks between its controls through to the row, as the bare siblings it replaced did.
      actions={!isCollapsed && (actions || hasChildren) ? (
        <span className="-my-1 flex shrink-0 items-center gap-2 pointer-events-none [&>*]:pointer-events-auto">
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
        </span>
      ) : undefined}
    >
      {/* `block` is what makes `truncate` work: overflow (and so the ellipsis) does not apply to an inline
          box, so an inline span sizes to its text and spills past the rail's edge instead of clipping. Its
          parent already carries `min-w-0 flex-1`, so a block child takes the constrained width. Visible on a
          long label in a narrow sidebar — a full email address in the profile row is the usual one. */}
      {!isCollapsed ? (
        <span className="block truncate">{label}</span>
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
