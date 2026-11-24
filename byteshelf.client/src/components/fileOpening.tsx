import { openTabWithoutPasswords } from '../lib/encryption/passwords';
import { saveBlob, saveFrom, showBlob, showsInTab } from '../lib/localBlobs';
import { runOrToast } from '../lib/utils';
import { isBlobBacked, type FileReading } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { showActionToast } from './actionToast';
import { NameLine } from './MiddleTruncatedName';

/** Opening a file in a new tab, and saving files. */
export interface FileOpening {
  open: (file: ShelfFile) => Promise<void>;
  save: (files: ShelfFile[]) => Promise<void>;
}

/** Whether opening a file in a tab shows it there: a blob of a type a tab must not show is saved instead. */
export function opensInTab(file: ShelfFile): boolean {
  return !isBlobBacked(file) || showsInTab(file.contentType);
}

/**
 * Opens and saves files of one source (a shelf's live files, or one deletion in its trash), never
 * through a public link. A plain file goes by navigating to it, the session's token in its cookie, so
 * it streams and the browser's own viewers handle any type. A blob-backed one (`isBlobBacked`) is
 * opened from its contents read into the page, shown in a tab only when its type is one a tab may show
 * (`showBlob`), and saved otherwise.
 */
export function fileOpening({ source, contents }: FileReading): FileOpening {
  /**
   * Opens a file in a new tab. The tab is opened before anything is awaited, while the click still
   * counts as the user's, or the browser would block it as a popup; a blocked tab saves the file.
   */
  async function open(file: ShelfFile): Promise<void> {
    if (isBlobBacked(file)) return openFromBlob(file);
    const tab = openTabWithoutPasswords();
    if (!tab) return save([file]);
    await runOrToast(async () => {
      try {
        tab.location.href = await source.navigationUrl(file.id, false);
      } catch (caught) {
        tab.close();
        throw caught;
      }
    }, 'Could not open the file');
  }

  /**
   * Opens a file from its contents read here. Reading, or asking for a password, can take long enough
   * for the browser to block the tab; a toast then opens it from a fresh click.
   */
  async function openFromBlob(file: ShelfFile): Promise<void> {
    await runOrToast(async () => {
      const blob = await contents(file);
      if (!blob || showBlob(blob, file.name)) return;
      showActionToast({
        message: <NameLine name={file.name} after={file.encrypted ? ' is unlocked' : ' is ready'} />,
        action: 'Open',
        onAction: () => showBlob(blob, file.name),
        duration: 15000,
      });
    }, 'Could not open the file');
  }

  /**
   * Saves files, each as a download rather than a navigation, so one starting does not cancel the one
   * before it; a browser may ask once before saving several.
   */
  async function save(files: ShelfFile[]): Promise<void> {
    await runOrToast(async () => {
      for (const file of files) {
        if (isBlobBacked(file)) {
          const blob = await contents(file);
          if (blob) saveBlob(blob, file.name);
        } else {
          saveFrom(await source.navigationUrl(file.id, true), '');
        }
      }
    }, files.length === 1 ? 'Could not save the file' : 'Could not save the files');
  }

  return { open, save };
}
