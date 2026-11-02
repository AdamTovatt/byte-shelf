import { openTabWithoutPasswords } from '../lib/encryption/passwords';
import { saveBlob, saveFrom, showBlob } from '../lib/localBlobs';
import { runOrToast } from '../lib/utils';
import { navigationUrl, opensByNavigation, readContent } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { showActionToast } from './actionToast';
import { NameLine } from './MiddleTruncatedName';

/** Opening a file in a new tab, and saving files. */
export interface FileOpening {
  open: (file: ShelfFile) => Promise<void>;
  save: (files: ShelfFile[]) => Promise<void>;
}

/**
 * Opens and saves a shelf's files, never through a public link. A plain file goes by navigating to it,
 * the session's token in its cookie, so it streams and the browser's own viewers handle any type. An
 * encrypted file, and any file where the page cannot keep that cookie (`opensByNavigation`), is read
 * here into a blob (decrypted by `decrypted` when encrypted), shown in a tab only when its type is one
 * a tab may show (`showBlob`), and saved otherwise.
 */
export function fileOpening(shelf: string, decrypted: (file: ShelfFile) => Promise<Blob | null>): FileOpening {
  function throughBlob(file: ShelfFile): boolean {
    return file.encrypted || !opensByNavigation();
  }

  /** The file's contents read here, decrypted when encrypted; null when no password was given. */
  function contentsHere(file: ShelfFile): Promise<Blob | null> {
    return file.encrypted ? decrypted(file) : readContent(shelf, file.id);
  }

  /**
   * Opens a file in a new tab. The tab is opened before anything is awaited, while the click still
   * counts as the user's, or the browser would block it as a popup; a blocked tab saves the file.
   */
  async function open(file: ShelfFile): Promise<void> {
    if (throughBlob(file)) return openFromBlob(file);
    const tab = openTabWithoutPasswords();
    if (!tab) return save([file]);
    await runOrToast(async () => {
      try {
        tab.location.href = await navigationUrl(shelf, file.id, false);
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
      const blob = await contentsHere(file);
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
        if (throughBlob(file)) {
          const blob = await contentsHere(file);
          if (blob) saveBlob(blob, file.name);
        } else {
          saveFrom(await navigationUrl(shelf, file.id, true), '');
        }
      }
    }, files.length === 1 ? 'Could not save the file' : 'Could not save the files');
  }

  return { open, save };
}
