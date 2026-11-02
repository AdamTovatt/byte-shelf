// What a drag of rows drops into (components/EntryDragAndDrop.tsx): a folder, named by its path.

/** The drop target id of the folder at `path`. */
export function folderDropId(path: string): string {
  return `folder:${path}`;
}

/** The folder a drop target id names; null for anything else. */
export function folderOfDropId(id: string | number | null | undefined): string | null {
  return typeof id === 'string' && id.startsWith('folder:') ? id.slice('folder:'.length) : null;
}
