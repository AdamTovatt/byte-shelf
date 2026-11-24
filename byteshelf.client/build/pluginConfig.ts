import { readFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import react from '@vitejs/plugin-react';
import autoprefixer from 'autoprefixer';
import type { InlineConfig, Plugin } from 'vite';
import svgr from 'vite-plugin-svgr';
import { ownClassesOnly } from './ownClasses.ts';
import { pluginStyles } from './pluginStyles.ts';
import { SHARED_MODULES, SHARED_PACKAGES, packageOf } from './sharedModules.ts';

/**
 * The Vite config of the plugin in `folder` (plugins/<id>): builds `src/plugin.tsx`, with what it
 * imports, into a plugin folder in `dist`, its version the server's. What the plugin shares with the app
 * stays an import the browser resolves, and everything else is addressed relative to the module, so the
 * plugin does not know where it is served.
 */
export function pluginConfig(folder: string): InlineConfig {
  const manifest = JSON.parse(readFileSync(join(folder, 'plugin.json'), 'utf8')) as { id: string };
  checkId(manifest.id, basename(folder));

  return {
    configFile: false,
    root: folder,
    base: './',
    publicDir: false,
    plugins: [svgr(), react(), sharedWithTheApp(), ownClassesOnly(), pluginManifest({ ...manifest, version: serverVersion() })],
    css: {
      postcss: { plugins: [pluginStyles(manifest.id, [join(folder, 'src/**/*.{ts,tsx}')]), autoprefixer()] },
    },
    worker: { format: 'es' },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
      // Where workers go, which the names below leave out.
      assetsDir: 'ui',
      target: 'es2022',
      modulePreload: false,
      // One stylesheet, for the app to load before it shows the plugin, whatever is lazy inside it.
      cssCodeSplit: false,
      rollupOptions: {
        input: join(folder, 'src/plugin.tsx'),
        preserveEntrySignatures: 'strict',
        external: [...SHARED_MODULES],
        output: {
          entryFileNames: 'ui/plugin.js',
          chunkFileNames: 'ui/[name]-[hash].js',
          assetFileNames: (asset) => (asset.names.some((name) => name.endsWith('.css')) ? 'ui/plugin.css' : 'ui/[name]-[hash][extname]'),
        },
      },
    },
  };
}

/**
 * Refuses an id the plugin cannot be released under, as byteshelf-<id>.zip, which is named after its
 * folder: updaemon picks the one asset ending in the machine's -<rid>.zip, so a plugin's that ends like a
 * server's would stop every installed server from updating.
 */
function checkId(id: string, folderName: string): void {
  if (id !== folderName) throw new Error(`plugin.json has the id ${id}; it must be its folder's name, ${folderName}.`);
  if (/-(linux|osx|win)(-[a-z0-9]+)*$/.test(`-${id}`)) throw new Error(`The plugin ${id} would be released under a name that reads as a server's zip.`);
}

/** The server's version, which is that of the plugins made here. */
function serverVersion(): string {
  const project = readFileSync(resolve(import.meta.dirname, '../../ByteShelf.Server/ByteShelf.Server.csproj'), 'utf8');
  const version = /<Version>([^<]+)<\/Version>/.exec(project)?.[1];
  if (!version) throw new Error('ByteShelf.Server.csproj has no <Version>.');
  return version;
}

/** Refuses a module of a shared package that is not shared itself: bundled, it would bring a second React. */
function sharedWithTheApp(): Plugin {
  return {
    name: 'byteshelf:shared-with-the-app',
    enforce: 'pre',
    resolveId(id, importer) {
      if (!SHARED_MODULES.includes(id) && SHARED_PACKAGES.has(packageOf(id))) {
        throw new Error(`${id}, which ${importer} imports, is not among the modules the app shares: ${SHARED_MODULES.join(', ')}.`);
      }
      return undefined;
    },
  };
}

/** Writes plugin.json beside the ui folder. */
function pluginManifest(manifest: object): Plugin {
  return {
    name: 'byteshelf:plugin-manifest',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'plugin.json', source: `${JSON.stringify(manifest, null, 2)}\n` });
    },
  };
}
