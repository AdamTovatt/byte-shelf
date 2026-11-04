import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { ERROR_CODES } from './errorCodes';

describe('error codes', () => {
  it('mirrors every code the server sends', () => {
    const testDirectory = dirname(fileURLToPath(import.meta.url));
    const source = readFileSync(resolve(testDirectory, '../../../ByteShelfCommon/ErrorCodes.cs'), 'utf8');

    const serverCodes = [...source.matchAll(/public const string \w+ = "([^"]+)";/g)].map((match) => match[1]);

    expect(serverCodes.length).toBeGreaterThan(0);
    expect(Object.values(ERROR_CODES).sort()).toEqual([...serverCodes].sort());
  });
});
