import { type HTMLAttributes, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Icon, type IconComponent } from './Icon';
import { cn } from '../../lib/utils';

// Trimmed from ordo.ui's TabBar: one indicator colour, and no tab actions, close buttons or
// activity dots, which nothing here uses.

/* ── Tab (pure UI — router-agnostic) ── */

export interface TabProps {
  /** Label text shown in the tab. */
  label: string;
  /** Optional icon shown before the label. */
  icon?: IconComponent;
  /** Mark the tab as active. */
  isActive?: boolean;
  /**
   * Forwarded verbatim to the button's `aria-current`. `RouteTab` passes `'page'` while the current URL
   * matches its `href`, which is where a screen reader learns which tab the page is on.
   */
  ariaCurrent?: 'page';
  onClick?: () => void;
  className?: string;
  labelClassName?: string;
}

export function Tab({ label, icon, isActive = false, ariaCurrent, onClick, className, labelClassName }: TabProps) {
  return (
    <button
      type="button"
      // Present whenever the tab is active, absent otherwise: a stable hook for a check that needs to
      // know which tab is current, rather than matching the classes that paint one.
      data-active={isActive || undefined}
      aria-current={ariaCurrent}
      // Named explicitly rather than by its visible text, because a caller may hide that text on a
      // narrow screen, and a CSS-hidden label contributes nothing to the accessible name.
      aria-label={label}
      onClick={onClick}
      className={cn(
        'relative flex h-9 min-w-0 items-center gap-1.5 rounded-t-md px-3 text-sm font-medium transition-colors',
        isActive ? 'bg-canvas text-fg' : 'text-muted hover:bg-row-hover hover:text-fg',
        className,
      )}
    >
      {icon && <Icon icon={icon} size={16} strokeWidth={2} className="shrink-0" />}
      {label && <span className={cn('truncate', labelClassName)}>{label}</span>}
      {/* The 2px bar under the selected tab. */}
      {isActive && <div className="absolute bottom-0 left-0 right-0 h-[2px] rounded-full bg-fg" />}
    </button>
  );
}

/* ── RouteTab (route-aware wrapper) ── */

export interface RouteTabProps extends Omit<TabProps, 'isActive' | 'onClick' | 'ariaCurrent'> {
  /** Route path — the tab is active when the current URL matches. */
  href: string;
}

export function RouteTab({ href, ...rest }: RouteTabProps) {
  const location = useLocation();
  const navigate = useNavigate();
  // One boolean feeds the paint, `data-active` and `aria-current` alike, so the three never disagree.
  const isActive = location.pathname === href || location.pathname.startsWith(href + '/');

  return <Tab {...rest} isActive={isActive} ariaCurrent={isActive ? 'page' : undefined} onClick={() => navigate(href)} />;
}

/* ── TabGroup ── */

export interface TabGroupProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
}

export function TabGroup({ children, className, ...props }: TabGroupProps) {
  return (
    <div className={cn('flex items-center gap-0.5', className)} {...props}>
      {children}
    </div>
  );
}
