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

/**
 * A quota typed as an amount of `unit`: its bytes, null when left empty, or undefined when what is
 * typed is not a size.
 */
export function tryParseQuota(amount: string, unit: QuotaUnit): number | null | undefined {
  const trimmed = amount.trim();
  if (trimmed === '') {
    return null;
  }

  const value = Number(trimmed);
  return Number.isFinite(value) && value >= 0 ? Math.round(value * QUOTA_UNIT_BYTES[unit]) : undefined;
}

/** A quota typed as an amount of `unit`, as bytes; null when left empty. The reverse of formatQuota. */
export function parseQuota(amount: string, unit: QuotaUnit): number | null {
  const bytes = tryParseQuota(amount, unit);
  if (bytes === undefined) {
    throw new Error(`The quota is a number of ${unit}, or empty for no limit`);
  }

  return bytes;
}

/** How many characters before the extension stay with it at the end, as macOS keeps them. */
const KEPT_BEFORE_EXTENSION = 6;

/**
 * What counts as an extension: a dot and up to eight letters or digits ending the name, such as
 * ".pdf" or ".docx". Anything longer after the last dot, or with spaces or dashes in it, is part of
 * the name ("v1.2 release notes"), so it can give way like the rest.
 */
const EXTENSION = /\.[\p{L}\p{N}]{1,8}$/u;

const characters = new Intl.Segmenter(undefined, { granularity: 'grapheme' });

/** The characters of `text` as a reader counts them, an emoji of several code points as one. */
function charactersOf(text: string): string[] {
  return Array.from(characters.segment(text), (each) => each.segment);
}

/**
 * Where a name is cut when it does not fit (MiddleTruncatedName): everything up to the last few
 * characters before its extension may give way to "…", and those characters with the extension always
 * show. A name without an extension keeps its last few characters. It is cut between characters as a
 * reader sees them, never inside an emoji.
 */
export function splitForMiddle(name: string): { start: string; end: string } {
  const all = charactersOf(name);
  const extension = EXTENSION.exec(name);
  const extensionLength = extension && extension.index > 0 ? charactersOf(extension[0]).length : 0;
  const cut = Math.max(0, all.length - extensionLength - KEPT_BEFORE_EXTENSION);
  return { start: all.slice(0, cut).join(''), end: all.slice(cut).join('') };
}

/** How much of a quota is used, 0 to 1; null without a quota. */
export function usedShare(usedBytes: number, quotaBytes: number | null | undefined): number | null {
  if (!hasQuota(quotaBytes)) {
    return null;
  }

  return quotaBytes === 0 ? 1 : Math.min(usedBytes / quotaBytes, 1);
}
