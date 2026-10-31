import type { ReactNode } from 'react';
import { Icon, type IconComponent } from './Icon';

export interface ListRowProps {
  icon?: IconComponent;
  /** Called when the row's main part is clicked. */
  onClick: () => void;
  /** The row's own buttons, beside the clickable part rather than inside it. */
  actions?: ReactNode;
  children: ReactNode;
}

/** A row in a list that opens something when clicked and has buttons of its own at its end. */
export function ListRow({ icon, onClick, actions, children }: ListRowProps) {
  return (
    <div className="flex items-center gap-3 rounded-lg px-3 py-2 transition-colors hover:bg-elevation-3-light dark:hover:bg-elevation-3-dark">
      <button type="button" onClick={onClick} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        {icon && <Icon icon={icon} size={18} className="shrink-0 text-text-3-light dark:text-text-3-dark" />}
        <span className="min-w-0 flex-1">{children}</span>
      </button>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  );
}
