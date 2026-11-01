import { describe, expect, it } from 'vitest';
import { describeUsage, formatBytes, formatDate, formatQuota, formatTime, parseQuota, usedShare } from './format';

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
  it('names the quota only when there is one', () => {
    expect(describeUsage({ usedBytes: 2048, quotaBytes: null, fileCount: 1 })).toBe('2 KiB used · 1 file');
    expect(describeUsage({ usedBytes: 0, quotaBytes: 1024, fileCount: 3 })).toBe('0 B of 1 KiB used · 3 files');
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
