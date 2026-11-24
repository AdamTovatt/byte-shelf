import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { afterEach, beforeEach, vi } from 'vitest';

/** How long a test that builds may take: builds are slow on a busy machine. */
export const BUILD_MS = 120_000;

export interface TestFolder {
  readonly path: string;
  write: (files: Record<string, string>) => void;
  read: (path: string) => string;
}

/**
 * Gives each test a folder called `name` to build in, deleted afterwards, with the build as a release's:
 * Vitest's own NODE_ENV is `test`. It lies in the client, so that it finds its packages where the client's
 * are, as a plugin's own would be.
 */
export function useTestFolder(name: string): TestFolder {
  let home = '';

  const folder: TestFolder = {
    get path() {
      return join(home, name);
    },
    write(files) {
      for (const [path, contents] of Object.entries(files)) {
        mkdirSync(dirname(join(folder.path, path)), { recursive: true });
        writeFileSync(join(folder.path, path), contents);
      }
    },
    read: (path) => readFileSync(join(folder.path, path), 'utf8'),
  };

  beforeEach(() => {
    vi.stubEnv('NODE_ENV', 'production');
    mkdirSync(resolve('test-results'), { recursive: true });
    home = mkdtempSync(resolve('test-results/build-'));
    mkdirSync(folder.path);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    rmSync(home, { recursive: true, force: true });
  });

  return folder;
}
