import { describe, expect, it } from 'vitest';
import { describeUsage, formatBytes, formatQuotaMegabytes, parseQuotaMegabytes, usedShare } from './format';

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

describe('parseQuotaMegabytes', () => {
  it('reads MiB as bytes, and empty as no quota', () => {
    expect(parseQuotaMegabytes(' ')).toBeNull();
    expect(parseQuotaMegabytes('1.5')).toBe(1572864);
    expect(parseQuotaMegabytes('0')).toBe(0);
  });

  it('refuses what is not a size', () => {
    expect(() => parseQuotaMegabytes('ten')).toThrow();
    expect(() => parseQuotaMegabytes('-1')).toThrow();
  });

  it('round-trips a quota through the form', () => {
    expect(formatQuotaMegabytes(null)).toBe('');
    expect(formatQuotaMegabytes(undefined)).toBe('');
    expect(formatQuotaMegabytes(1572864)).toBe('1.5');
    expect(parseQuotaMegabytes(formatQuotaMegabytes(1572864))).toBe(1572864);
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
