const SIZE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];

/** A size as people read it: 1023 B, 1 KiB, 1.5 MiB. */
export function formatBytes(bytes: number): string {
  let size = bytes;
  let unit = 0;
  while (size >= 1024 && unit < SIZE_UNITS.length - 1) {
    size /= 1024;
    unit++;
  }

  return unit === 0 ? `${bytes} B` : `${Number(size.toFixed(1))} ${SIZE_UNITS[unit]}`;
}

/** An instant from the server, in the reader's own time zone. */
export function formatTime(instant: string): string {
  return new Date(instant).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

const MEBIBYTE = 1024 * 1024;

/** Whether a shelf has a quota: the server leaves it out or sends null when it has none. */
function hasQuota(quotaBytes: number | null | undefined): quotaBytes is number {
  return quotaBytes !== null && quotaBytes !== undefined;
}

/** A shelf's usage in one line: "12 MiB of 1 GiB used · 3 files". */
export function describeUsage(shelf: { usedBytes: number; quotaBytes?: number | null; fileCount: number }): string {
  const quota = hasQuota(shelf.quotaBytes) ? ` of ${formatBytes(shelf.quotaBytes)}` : '';
  return `${formatBytes(shelf.usedBytes)}${quota} used · ${shelf.fileCount} ${shelf.fileCount === 1 ? 'file' : 'files'}`;
}

/** A quota in MiB, as typed into a form; empty without a quota. The reverse of parseQuotaMegabytes. */
export function formatQuotaMegabytes(quotaBytes: number | null | undefined): string {
  return hasQuota(quotaBytes) ? String(quotaBytes / MEBIBYTE) : '';
}

/** A quota typed in MiB, as bytes; null when left empty. */
export function parseQuotaMegabytes(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === '') {
    return null;
  }

  const megabytes = Number(trimmed);
  if (!Number.isFinite(megabytes) || megabytes < 0) {
    throw new Error('The quota is a number of MiB, or empty for no limit');
  }

  return Math.round(megabytes * MEBIBYTE);
}

/** How much of a quota is used, 0 to 1; null without a quota. */
export function usedShare(usedBytes: number, quotaBytes: number | null | undefined): number | null {
  if (!hasQuota(quotaBytes)) {
    return null;
  }

  return quotaBytes === 0 ? 1 : Math.min(usedBytes / quotaBytes, 1);
}
