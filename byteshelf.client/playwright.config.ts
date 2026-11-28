import { mkdirSync, rmSync, symlinkSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';
import { ADMIN_EMAIL, ADMIN_PASSWORD, PORT } from './e2e/e2eConfig';

// The browser test runs the real server on a root of its own, serving this client's production
// build, so what it shows is what an install shows, with the plugins below as their builds leave them and
// the album plugin, the tests' own.
// The runner empties the root before the server starts. This file is loaded again by the worker,
// which inherits the runner's environment, so the variable keeps it from emptying the root again.
const ROOT = resolve('e2e-root');
/** The plugins made in plugins/ that the server has. */
const PLUGINS = ['markdown', 'images', 'video'];
if (!process.env.BYTESHELF_E2E_ROOT) {
  rmSync(ROOT, { recursive: true, force: true });
  mkdirSync(resolve(ROOT, 'plugins'), { recursive: true });
  mkdirSync(resolve(ROOT, 'instance'));
  // Windows links a folder without special rights only as a junction.
  for (const plugin of PLUGINS) symlinkSync(resolve(`../plugins/${plugin}/dist`), resolve(ROOT, 'plugins', plugin), 'junction');
  symlinkSync(resolve('e2e/albumPlugin'), resolve(ROOT, 'plugins/album'), 'junction');
  process.env.BYTESHELF_E2E_ROOT = ROOT;
}

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: `http://127.0.0.1:${PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: [
      'npm run build',
      ...PLUGINS.map((plugin) => `npm ci --prefix ../plugins/${plugin} --prefer-offline --no-audit --no-fund && npm run build --prefix ../plugins/${plugin}`),
      'dotnet run --project ../ByteShelf.Server --no-launch-profile',
    ].join(' && '),
    url: `http://127.0.0.1:${PORT}/api/version/current`,
    timeout: 240_000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: `http://127.0.0.1:${PORT}`,
      ASPNETCORE_WEBROOT: resolve('dist'),
      BYTESHELF_ROOT: resolve(ROOT, 'instance'),
      BYTESHELF_PLUGINS: resolve(ROOT, 'plugins'),
      INITIAL_ADMIN_EMAIL: ADMIN_EMAIL,
      INITIAL_ADMIN_PASSWORD: ADMIN_PASSWORD,
    },
  },
});
