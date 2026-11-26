import type { ReactNode } from 'react';
import { Dropdown, iconMap } from '../../ui';
import { OfferedActionItems, type FileActionsProps } from '../FileActionItems';
import { BarButton } from './FloatingBars';

/** A menu of the plugins' actions for the file previewed, in its bar; none without any. */
export function FileActionsMenu({ actions, target }: FileActionsProps): ReactNode {
  const offered = actions.actionsFor(target.file);
  return offered.length > 0 && (
    <Dropdown align="right" trigger={<BarButton icon={iconMap.more} aria-label="More actions" title="More actions" />}>
      <OfferedActionItems offered={offered} actions={actions} target={target} />
    </Dropdown>
  );
}
