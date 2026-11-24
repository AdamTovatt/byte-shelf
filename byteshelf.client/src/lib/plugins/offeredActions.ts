import type { ShelfFile } from '../../services/shelfService';
import { logFailureOnce } from '../logFailureOnce';
import type { FileAction } from './fileAction';
import type { RunningPlugin } from './pluginHost';

/** A plugin's action as a menu offers it. */
export interface OfferedAction {
  pluginId: string;
  /** Apart from every other plugin's actions too. */
  key: string;
  action: FileAction;
}

/** The actions `plugins` have for `file`, in order of plugin and then as the plugin lists them. */
export function fileActionsFor(plugins: readonly RunningPlugin[], file: ShelfFile): OfferedAction[] {
  return plugins.flatMap(({ id, fileActions }) => fileActions
    .filter((action) => {
      try {
        return action.showsFor(file);
      } catch (caught) {
        // One that fails is not there, rather than taking the whole menu with it.
        logFailureOnce(action, `The action ${action.id} of the plugin ${id} could not be asked whether it shows.`, caught);
        return false;
      }
    })
    .map((action) => ({ pluginId: id, key: `${id} ${action.id}`, action })));
}
