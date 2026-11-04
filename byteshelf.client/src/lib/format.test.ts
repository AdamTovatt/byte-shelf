import { describe, expect, it } from 'vitest';
import { describeUsage, formatBytes, formatDate, formatQuota, formatTime, parseQuota, splitForMiddle, tryParseQuota, usedShare } from './format';

describe('formatBytes', () => {
  it.each([
    [0, '0 B'],
    [1023, '1023 B'],
    [1024, '1 KiB'],
    [1536, '1.5 KiB'],
    [1024 * 1024, '1 MiB'],
  ])('writes %i bytes as %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });
});

describe('formatDate and formatTime', () => {
  it('write the reader-local day and 24-hour time, zero-padded', () => {
    const instant = new Date(2026, 2, 1, 9, 5).toISOString();
    expect(formatDate(instant)).toBe('2026-03-01');
    expect(formatTime(instant)).toBe('2026-03-01 09:05');
  });
});

describe('parseQuota', () => {
  it('reads an amount of the unit as bytes, and empty as no quota', () => {
    expect(parseQuota(' ', 'GiB')).toBeNull();
    expect(parseQuota('1.5', 'MiB')).toBe(1572864);
    expect(parseQuota('2', 'GiB')).toBe(2 * 1024 * 1024 * 1024);
    expect(parseQuota('0', 'MiB')).toBe(0);
  });

  it('refuses what is not a size', () => {
    expect(() => parseQuota('ten', 'MiB')).toThrow();
    expect(() => parseQuota('-1', 'GiB')).toThrow();
  });
});

describe('formatQuota', () => {
  it('writes whole GiB in GiB, anything else in MiB, and no quota as empty', () => {
    expect(formatQuota(null)).toEqual({ amount: '', unit: 'GiB' });
    expect(formatQuota(undefined)).toEqual({ amount: '', unit: 'GiB' });
    expect(formatQuota(3 * 1024 * 1024 * 1024)).toEqual({ amount: '3', unit: 'GiB' });
    expect(formatQuota(1572864)).toEqual({ amount: '1.5', unit: 'MiB' });
    expect(formatQuota(0)).toEqual({ amount: '0', unit: 'MiB' });
  });

  it('round-trips a quota through the form', () => {
    for (const bytes of [1572864, 5 * 1024 * 1024 * 1024, 0]) {
      const { amount, unit } = formatQuota(bytes);
      expect(parseQuota(amount, unit)).toBe(bytes);
    }
  });
});

describe('describeUsage', () => {
  const MiB = 1024 * 1024;

  it('without a quota, says only what is used', () => {
    expect(describeUsage({ usedBytes: 2048, quotaBytes: null, fileCount: 1 })).toBe('2 KiB used · 1 file');
    expect(describeUsage({ usedBytes: 14.7 * MiB, quotaBytes: undefined, fileCount: 6 })).toBe('14.7 MiB used · 6 files');
  });

  it.each([
    [14.7 * MiB, 1000 * MiB, '1% used (14.7 MiB / 1000 MiB)'],
    [0, 1000 * MiB, '0% used (0 B / 1000 MiB)'],
    [1, 1000 * MiB, '<1% used (1 B / 1000 MiB)'],
    [9.9 * MiB, 1000 * MiB, '<1% used (9.9 MiB / 1000 MiB)'],
    [10 * MiB, 1000 * MiB, '1% used (10 MiB / 1000 MiB)'],
    [125 * MiB, 1000 * MiB, '13% used (125 MiB / 1000 MiB)'],
    [1500 * MiB, 1000 * MiB, '150% used (1.5 GiB / 1000 MiB)'],
    [1, 0, '100% used (1 B / 0 B)'],
  ])('with a quota, %d of %d bytes reads "%s"', (usedBytes, quotaBytes, expected) => {
    expect(describeUsage({ usedBytes, quotaBytes, fileCount: 6 })).toBe(`${expected} · 6 files`);
  });
});

describe('usedShare', () => {
  it('is null without a quota and capped at the whole', () => {
    expect(usedShare(10, null)).toBeNull();
    expect(usedShare(25, 100)).toBe(0.25);
    expect(usedShare(150, 100)).toBe(1);
    expect(usedShare(0, 0)).toBe(1);
  });
});

describe('splitForMiddle', () => {
  it.each([
    ['quarterly-report-final-2026.pdf', 'quarterly-report-fina', 'l-2026.pdf'],
    ['README', '', 'README'],
    ['a-long-name-without-extension', 'a-long-name-without-ext', 'ension'],
    ['.env', '', '.env'],
    ['/2026/march/receipt.pdf', '/2026/march/r', 'eceipt.pdf'],
    // What follows the last dot here is no extension, so it gives way as the rest of the name does.
    ['v1.2 release notes for the team', 'v1.2 release notes for th', 'e team'],
    ['backup.from-2026-03-01-nightly', 'backup.from-2026-03-01-n', 'ightly'],
  ])('keeps the end of %s', (name, start, end) => {
    expect(splitForMiddle(name)).toEqual({ start, end });
  });

  it('cuts between emoji, never inside one', () => {
    expect(splitForMiddle(`party-${'🎉'.repeat(8)}.png`)).toEqual({ start: 'party-🎉🎉', end: `${'🎉'.repeat(6)}.png` });
    const family = '👨‍👩‍👧';
    expect(splitForMiddle(`x${family.repeat(7)}`)).toEqual({ start: `x${family}`, end: family.repeat(6) });
  });
});

describe('tryParseQuota', () => {
  it('answers undefined for what is not a size, rather than throwing', () => {
    expect(tryParseQuota('ten', 'MiB')).toBeUndefined();
    expect(tryParseQuota('-1', 'GiB')).toBeUndefined();
    expect(tryParseQuota('', 'GiB')).toBeNull();
    expect(tryParseQuota('2', 'MiB')).toBe(2 * 1024 * 1024);
  });
});
