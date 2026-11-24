import type { ReactNode } from 'react';
import type { ContentReader } from '../content/contentReader';
import type { ShelfFile } from '../../services/shelfService';
import type { IconComponent } from '../../ui';

/** What a file action is run with. */
export interface FileActionContext {
  /** The name of the shelf the file is on. */
  shelf: string;
  file: ShelfFile;
  /**
   * The file's contents, until the action is done and no dialog of it is open. Reading an encrypted file
   * asks for its password, and fails with an `AbortError` when none is given.
   */
  content: ContentReader;
  /**
   * Shows a dialog of the plugin's own, such as a `Modal`, until it calls `close` or the person leaves the
   * shelf; a `lazy` component in it loads as it is shown. One dialog may show another: before it closes
   * itself, when that one is to read the contents too.
   */
  showDialog: (dialog: (close: () => void) => ReactNode) => void;
}

/** An item of a live file's menu: in a listing, in search results and in the preview. */
export interface FileAction {
  /** Apart from the plugin's other actions. */
  id: string;
  label: string;
  icon?: IconComponent;
  /** Whether it is there for `file`, whose contents may be encrypted. Asked as the menu is drawn, so it answers at once. */
  showsFor: (file: ShelfFile) => boolean;
  /** Does it; what it throws, but for an `AbortError`, the person is told. */
  run: (context: FileActionContext) => void | Promise<void>;
}
