import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { PluginDialog, type PluginDialogProps } from '../components/PluginDialog';
import { fileActionsFor, type OfferedAction } from '../lib/plugins/fileActions';
import type { ClosableReader } from '../lib/content/contentReader';
import { openableReader } from '../lib/content/openableReader';
import { isAbortError } from '../lib/abort';
import { runOrToast } from '../lib/utils';
import type { FileAction } from '../lib/plugins/fileAction';
import type { OpenableFile } from '../services/fileReading';
import type { ShelfFile } from '../services/shelfService';
import { usePluginHost } from './usePluginHost';

/** The plugins' file actions on one shelf. */
export interface FileActions {
  /** Those a file has. */
  actionsFor: (file: ShelfFile) => OfferedAction[];
  run: (action: FileAction, target: OpenableFile) => Promise<void>;
}

interface ShownDialog extends PluginDialogProps {
  id: number;
}

/** The plugins' actions for the live files of a shelf, and the dialogs of those run here, to render; null when they show none. */
export function useFileActions(shelf: string): { actions: FileActions; dialogs: ReactNode } {
  const { plugins } = usePluginHost();
  const [dialogs, setDialogs] = useState<readonly ShownDialog[]>([]);
  const dialogCount = useRef(0);

  // Closed with the page, whose dialogs go without being closed.
  const reading = useRef(new Set<ClosableReader>());
  useEffect(() => {
    const readers = reading.current;
    return () => readers.forEach((reader) => reader.close());
  }, []);

  const run = useCallback(async (action: FileAction, target: OpenableFile): Promise<void> => {
    const content = openableReader(target);
    reading.current.add(content);
    const closing: Promise<void>[] = [];

    function showDialog(render: PluginDialogProps['render']): void {
      closing.push(new Promise((resolve) => {
        const id = ++dialogCount.current;
        function close(): void {
          setDialogs((shown) => shown.filter((each) => each.id !== id));
          resolve();
        }
        setDialogs((shown) => [...shown, { id, title: action.label, render, close }]);
      }));
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
    reading.current.delete(content);
    content.close();
  }, [shelf]);

  return {
    actions: { actionsFor: (file) => fileActionsFor(plugins, file), run },
    dialogs: dialogs.length === 0 ? null : dialogs.map(({ id, ...dialog }) => <PluginDialog key={id} {...dialog} />),
  };
}
