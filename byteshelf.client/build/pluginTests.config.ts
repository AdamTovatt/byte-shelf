import { resolve } from 'node:path';
import { defineConfig } from 'vitest/config';
import { SHARED_PACKAGES } from './sharedModules.ts';

const app = resolve(import.meta.dirname, '..');
const npmPackages = [...SHARED_PACKAGES].filter((name) => name !== '@byteshelf/plugin');

/** The Vitest config of the plugin in the working directory: what it shares with the app is the app's. */
export default defineConfig({
  root: process.cwd(),
  server: { fs: { allow: [resolve(app, '..')] } },
  resolve: {
    alias: [
      // Ahead of the package itself, which would take it as a path in its module.
      { find: '@byteshelf/plugin/testing', replacement: resolve(app, 'src/plugin/testing.ts') },
      { find: '@byteshelf/plugin', replacement: resolve(app, 'src/plugin/index.ts') },
      { find: new RegExp(`^(${npmPackages.join('|')})(?=/|$)`), replacement: `${app}/node_modules/$1` },
    ],
  },
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.test.{ts,tsx}'],
    // The plugin's own packages, so that the React they import is the app's too.
    server: { deps: { inline: [/\/plugins\/[^/]+\/node_modules\//] } },
  },
});
