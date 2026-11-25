/** How a file's row in a listing is laid out, which an upload's row shares, so it reads as the file that is coming. */
export const FILE_ROW = {
  /** The row, holding its main part and its actions at the end. */
  row: 'flex items-center gap-2 px-2 py-1',
  /** Its icon, name and size. */
  main: 'flex min-w-0 flex-1 items-center gap-3 rounded-lg px-2 py-2',
  name: 'min-w-0 truncate text-body-md text-fg',
  /** Where its size is, at the main part's end. */
  size: 'ml-auto shrink-0 pl-2 text-body-sm text-muted',
} as const;
