import { isMissed, isUnderway, type MissedState, type Upload, type UploadReporter, type UploadState } from '../services/uploadQueue';
import type { ShelfFile } from '../services/shelfService';

/** What a folder's list makes of an upload into it: its row's place, and whether that is the place of a file there. */
export interface ListedUpload {
  upload: Upload;
  /** Whether it goes over the file of its name, whose row it then takes. */
  replacing: boolean;
  /** The file it was stored as, once it is done, which its row then is, and when it was done. */
  stored: { file: ShelfFile; at: number } | null;
}

/**
 * The uploads to show, by the folder each goes into, as the queue reports them. A folder's list is read apart
 * from each upload's state, so that what draws the list is drawn again only as one is added or removed,
 * replaces a file or is done, and a row alone as its upload moves on.
 */
export interface UploadStore {
  report: UploadReporter;
  subscribe: (listener: () => void) => () => void;
  /** The uploads into a folder, in the order they were started: the same list until one is added, removed, replaces or is done. */
  inFolder: (shelf: string, folderPath: string) => readonly ListedUpload[];
  /** Where an upload is; null once it is gone. */
  stateOf: (id: number) => UploadState | null;
  /** Takes an upload away: one whose name was taken or that failed, dismissed, or one done that its folder's listing has caught up with. */
  remove: (id: number) => void;
  /**
   * Marks a folder shown, until what this answers is called. One whose rows a search hides is not shown, so a
   * taken name or a failure there is told of by `missed` too.
   */
  show: (shelf: string, folderPath: string) => () => void;
}

const NONE: readonly ListedUpload[] = [];

/** What tells the uploads into one folder from those into another. */
function uploadsKey(shelf: string, folderPath: string): string {
  return `${shelf}\n${folderPath}`;
}

/**
 * Holds uploads until each is cancelled, dismissed or, once done, taken away; `missed` is told of one whose name
 * was taken or that failed while its folder was not shown, which would otherwise go unseen.
 *
 * One done into a folder not shown is kept until the folder is shown again and its listing catches up, rather
 * than let go at once: the listing shown then, read before it was stored, would otherwise lack it until it is read
 * again. Kept for a folder never shown again, it is a few small records until the page is left.
 */
export function uploadStore(missed: (upload: Upload, state: MissedState) => void, now: () => number = Date.now): UploadStore {
  const uploads = new Map<number, { upload: Upload; state: UploadState }>();
  const folders = new Map<string, readonly ListedUpload[]>();
  const shown = new Map<string, number>();
  const listeners = new Set<() => void>();

  function changed(): void {
    for (const listener of listeners) listener();
  }

  /** Puts an upload in its folder's list, or takes it out with `listed` null; a list that would not change stays as it is. */
  function relist(upload: Upload, listed: ListedUpload | null): void {
    const key = uploadsKey(upload.shelf, upload.folderPath);
    const list = folders.get(key) ?? NONE;
    const was = list.find((item) => item.upload.id === upload.id);
    if (listed && was && was.replacing === listed.replacing && was.stored?.file === listed.stored?.file) return;

    const next = !listed
      ? list.filter((item) => item !== was)
      : was ? list.map((item) => (item === was ? listed : item)) : [...list, listed];
    if (next.length === 0) folders.delete(key);
    else folders.set(key, next);
  }

  function remove(id: number): void {
    const held = uploads.get(id);
    if (!held) return;
    uploads.delete(id);
    relist(held.upload, null);
    changed();
  }

  return {
    report(upload, state) {
      if (state.status === 'cancelled') {
        remove(upload.id);
        return;
      }

      uploads.set(upload.id, { upload, state });
      relist(upload, {
        upload,
        replacing: isUnderway(state) && state.replacing,
        stored: state.status === 'done' ? { file: state.file, at: now() } : null,
      });
      if (isMissed(state) && !shown.has(uploadsKey(upload.shelf, upload.folderPath))) missed(upload, state);
      changed();
    },

    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    inFolder: (shelf, folderPath) => folders.get(uploadsKey(shelf, folderPath)) ?? NONE,
    stateOf: (id) => uploads.get(id)?.state ?? null,
    remove,

    show(shelf, folderPath) {
      const key = uploadsKey(shelf, folderPath);
      shown.set(key, (shown.get(key) ?? 0) + 1);
      return () => {
        const left = shown.get(key)! - 1;
        if (left === 0) shown.delete(key);
        else shown.set(key, left);
      };
    },
  };
}
