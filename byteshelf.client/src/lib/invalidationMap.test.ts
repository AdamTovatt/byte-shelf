import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { RESOURCE_TO_QUERY_KEYS } from './invalidationMap';

// A backend resource constant with no RESOURCE_TO_QUERY_KEYS entry silently stops cache
// invalidation for that resource — nothing errors, mutations just stop propagating to other
// clients. This test parses the backend constants file so the drift is caught at test time.
describe('invalidation map', () => {
  it('mirrors every backend invalidation resource', () => {
    const testDirectory = dirname(fileURLToPath(import.meta.url));
    const backendResourcesPath = resolve(
      testDirectory,
      '../../../ByteShelf.Server/Invalidation/Models/InvalidationResources.cs',
    );
    const source = readFileSync(backendResourcesPath, 'utf8');

    const backendResources = [...source.matchAll(/public const string \w+ = "([^"]+)";/g)].map(
      (match) => match[1],
    );

    expect(backendResources.length).toBeGreaterThan(0);
    expect(Object.keys(RESOURCE_TO_QUERY_KEYS).sort()).toEqual([...backendResources].sort());
  });
});
