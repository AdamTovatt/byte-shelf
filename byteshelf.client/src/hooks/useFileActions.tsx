import { useCallback } from 'react';
import { PluginDialog } from '../components/PluginDialog';
import { useShowDialog } from '../contexts/DialogHost';
import { fileActionsFor, type OfferedAction } from '../lib/plugins/fileActions';
import type { ClosableReader } from '../lib/content/contentReader';
import { openableReader } from '../lib/content/openableReader';
import { isAbortError } from '../lib/abort';
import { runOrToast } from '../lib/utils';
import type { DrawDialog } from '../lib/drawDialog';
import type { FileAction } from '../lib/plugins/fileAction';
import type { OpenableFile } from '../services/fileReading';
import type { ShelfFile } from '../services/shelfService';
import { useClosedWhenGone } from './useClosedWhenGone';
import { usePluginHost } from './usePluginHost';

/** The plugins' file actions on one shelf. */
export interface FileActions {
  /** Those a file has. */
  actionsFor: (file: ShelfFile) => OfferedAction[];
  run: (action: FileAction, target: OpenableFile) => Promise<void>;
}

/** The plugins' actions for the live files of a shelf. */
export function useFileActions(shelf: string): FileActions {
  const { plugins } = usePluginHost();
  const show = useShowDialog();

  // Closed with the page, also while an action is still at it.
  const reading = useClosedWhenGone<ClosableReader>();

  const run = useCallback(async (action: FileAction, target: OpenableFile): Promise<void> => {
    const content = openableReader(target);
    reading.add(content);
    const closing: Promise<void>[] = [];

    function showDialog(draw: DrawDialog): void {
      closing.push(show((close) => <PluginDialog title={action.label} draw={draw} close={close} />).closed);
    }

    await runOrToast(async () => {
      try {
        await action.run({ shelf, file: target.file, content, showDialog });
      } catch (caught) {
        // Given up by the person, such as at the password prompt: they know.
        if (!isAbortError(caught)) throw caught;
      }
    }, `${action.label} failed`);

    // One may show another before it closes.
    for (let closed = closing.shift(); closed; closed = closing.shift()) await closed;
    reading.delete(content);
    content.close();
  }, [shelf, show, reading]);

  return { actionsFor: (file) => fileActionsFor(plugins, file), run };
}
