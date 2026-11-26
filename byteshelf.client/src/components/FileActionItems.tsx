import type { ReactNode } from 'react';
import { DropdownItem, Icon } from '../ui';
import type { FileActions } from '../hooks/useFileActions';
import type { OpenableFile } from '../services/fileReading';
import type { OfferedAction } from '../lib/plugins/offeredActions';
import { ErrorBoundary } from './ErrorBoundary';

export interface FileActionsProps {
  actions: FileActions;
  target: OpenableFile;
}

/** The plugins' actions for a file, as items of its menu. */
export function FileActionItems({ actions, target }: FileActionsProps): ReactNode {
  return <OfferedActionItems offered={actions.actionsFor(target.file)} actions={actions} target={target} />;
}

/** Those of the plugins' actions for a file that are `offered`, as items of its menu. */
export function OfferedActionItems({ offered, actions: { run }, target }: FileActionsProps & { offered: OfferedAction[] }): ReactNode {
  return offered.map((each) => {
    const { key, action } = each;
    return (
      <ErrorBoundary key={key} fallback={null}>
        <DropdownItem icon={action.icon && <Icon icon={action.icon} size={16} />} onClick={() => void run(each, target)}>{action.label}</DropdownItem>
      </ErrorBoundary>
    );
  });
}
