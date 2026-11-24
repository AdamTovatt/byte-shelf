import type { ReactNode } from 'react';
import { Dropdown, DropdownItem, Icon, IconButton, iconMap } from '../ui';
import type { FileActions } from '../hooks/useFileActions';
import type { OpenableFile } from '../services/fileReading';
import type { OfferedAction } from '../lib/plugins/fileActions';
import { ErrorBoundary } from './ErrorBoundary';

interface FileActionsProps {
  actions: FileActions;
  target: OpenableFile;
}

/** The plugins' actions for a file, as items of its menu. */
export function FileActionItems({ actions, target }: FileActionsProps): ReactNode {
  return itemsOf(actions.actionsFor(target.file), { actions, target });
}

/** A menu of the plugins' actions for a file; none without any. */
export function FileActionsMenu({ actions, target }: FileActionsProps): ReactNode {
  const offered = actions.actionsFor(target.file);
  return offered.length > 0 && (
    <Dropdown align="right" trigger={<IconButton variant="ghost" size="sm" icon={iconMap.more} aria-label="More actions" title="More actions" />}>
      {itemsOf(offered, { actions, target })}
    </Dropdown>
  );
}

function itemsOf(offered: OfferedAction[], { actions: { run }, target }: FileActionsProps): ReactNode {
  return offered.map(({ key, action }) => (
    <ErrorBoundary key={key} fallback={null}>
      <DropdownItem icon={action.icon && <Icon icon={action.icon} size={16} />} onClick={() => void run(action, target)}>{action.label}</DropdownItem>
    </ErrorBoundary>
  ));
}
