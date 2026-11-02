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

function twoDigits(value: number): string {
  return String(value).padStart(2, '0');
}

/** A day from the server in the reader's own time zone, the same in every locale: 2026-03-01. */
export function formatDate(instant: string): string {
  const date = new Date(instant);
  return `${date.getFullYear()}-${twoDigits(date.getMonth() + 1)}-${twoDigits(date.getDate())}`;
}

/** An instant from the server in the reader's own time zone, on a 24-hour clock: 2026-03-01 14:05. */
export function formatTime(instant: string): string {
  const date = new Date(instant);
  return `${formatDate(instant)} ${twoDigits(date.getHours())}:${twoDigits(date.getMinutes())}`;
}

const MEBIBYTE = 1024 * 1024;

/** Whether a shelf has a quota: the server leaves it out or sends null when it has none. */
function hasQuota(quotaBytes: number | null | undefined): quotaBytes is number {
  return quotaBytes !== null && quotaBytes !== undefined;
}

/**
 * A shelf's usage in one line, the one wording every place shows it in:
 * "1% used (14.7 MiB / 1000 MiB) · 6 files", or "14.7 MiB used · 6 files" without a quota.
 */
export function describeUsage(shelf: { usedBytes: number; quotaBytes?: number | null; fileCount: number }): string {
  const used = hasQuota(shelf.quotaBytes)
    ? `${formatPercent(shelf.usedBytes, shelf.quotaBytes)} used (${formatBytes(shelf.usedBytes)} / ${formatBytes(shelf.quotaBytes)})`
    : `${formatBytes(shelf.usedBytes)} used`;
  return `${used} · ${shelf.fileCount} ${shelf.fileCount === 1 ? 'file' : 'files'}`;
}

/** How much of a quota is used, to the whole percent: "<1%" for some but under one, and past 100% when over it. */
function formatPercent(usedBytes: number, quotaBytes: number): string {
  if (usedBytes <= 0) {
    return '0%';
  }

  // A quota of nothing is full by any use.
  const percent = quotaBytes === 0 ? 100 : (usedBytes / quotaBytes) * 100;
  return percent < 1 ? '<1%' : `${Math.round(percent)}%`;
}

/** The units a quota is typed in. */
export type QuotaUnit = 'MiB' | 'GiB';

export const QUOTA_UNITS: QuotaUnit[] = ['MiB', 'GiB'];

const QUOTA_UNIT_BYTES: Record<QuotaUnit, number> = { MiB: MEBIBYTE, GiB: 1024 * MEBIBYTE };

/** A quota as typed into a form: in GiB when it is whole GiB, else in MiB; empty without a quota. */
export function formatQuota(quotaBytes: number | null | undefined): { amount: string; unit: QuotaUnit } {
  if (!hasQuota(quotaBytes)) {
    return { amount: '', unit: 'GiB' };
  }

  const unit: QuotaUnit = quotaBytes > 0 && quotaBytes % QUOTA_UNIT_BYTES.GiB === 0 ? 'GiB' : 'MiB';
  return { amount: String(quotaBytes / QUOTA_UNIT_BYTES[unit]), unit };
}

/** A quota typed as an amount of `unit`, as bytes; null when left empty. The reverse of formatQuota. */
export function parseQuota(amount: string, unit: QuotaUnit): number | null {
  const trimmed = amount.trim();
  if (trimmed === '') {
    return null;
  }

  const value = Number(trimmed);
  if (!Number.isFinite(value) || value < 0) {
    throw new Error(`The quota is a number of ${unit}, or empty for no limit`);
  }

  return Math.round(value * QUOTA_UNIT_BYTES[unit]);
}

/** How much of a quota is used, 0 to 1; null without a quota. */
export function usedShare(usedBytes: number, quotaBytes: number | null | undefined): number | null {
  if (!hasQuota(quotaBytes)) {
    return null;
  }

  return quotaBytes === 0 ? 1 : Math.min(usedBytes / quotaBytes, 1);
}
