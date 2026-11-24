import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import type { Plugin, Rollup } from 'vite';

interface SharedModule {
  name: string;
  /** Where the app has it, for the one that is the app's own. */
  source?: string;
  /** A CommonJS package, whose names a module that passes everything on has to spell out. */
  isCommonJs?: boolean;
  /** Packages it passes on parts of, which bring a second copy of those when imported themselves. */
  passesOn?: readonly string[];
}

// plugins/tsconfig.base.json gives each its types.
const SHARED: readonly SharedModule[] = [
  { name: 'react', isCommonJs: true },
  { name: 'react/jsx-runtime', isCommonJs: true },
  { name: 'react-dom', isCommonJs: true },
  { name: '@tanstack/react-query' },
  { name: 'react-router-dom', passesOn: ['react-router'] },
  { name: '@byteshelf/plugin', source: '/src/plugin/index.ts' },
];

/**
 * The modules a plugin gets from the app rather than bringing its own, so there is one React and one of
 * each context whoever draws. Part of what a plugin's `api` promises: raise it when one goes.
 */
export const SHARED_MODULES: readonly string[] = SHARED.map(({ name }) => name);

/** The packages of which a plugin may import only the modules in SHARED_MODULES. */
export const SHARED_PACKAGES: ReadonlySet<string> = new Set(SHARED.flatMap(({ name, passesOn = [] }) => [packageOf(name), ...passesOn]));

/** The package the module `name` is of. */
export function packageOf(name: string): string {
  return name.split('/').slice(0, name.startsWith('@') ? 2 : 1).join('/');
}

const PREFIX = '\0byteshelf-shared:';

const require = createRequire(import.meta.url);

/** A module exporting all that the shared module `name` exports. */
function reExport(name: string): string {
  const shared = SHARED.find((each) => each.name === name);
  if (!shared?.isCommonJs) return `export * from '${shared?.source ?? name}';`;

  const names = Object.keys(require(name) as object).filter((each) => each !== 'default' && /^[A-Za-z_$][\w$]*$/.test(each));
  return `import all from '${name}';\nexport default all;\nexport const { ${names.join(', ')} } = all;`;
}

function importMap(addressOf: (name: string) => string) {
  const imports = Object.fromEntries(SHARED_MODULES.map((name) => [name, addressOf(name)]));
  return [{ tag: 'script', attrs: { type: 'importmap' }, children: JSON.stringify({ imports }, null, 2), injectTo: 'head-prepend' as const }];
}

/**
 * Makes the shared modules reachable to plugins, which the browser imports as they are: each gets an
 * entry of its own, sharing its chunks with the app's, and index.html an import map from the bare names
 * to them. In the dev server the entries are virtual modules.
 */
export function sharedModules(): Plugin {
  let isBuild = false;

  return {
    name: 'byteshelf:shared-modules',
    config({ root = '.' }, { command }) {
      isBuild = command === 'build';
      if (!isBuild) return undefined;
      const entries = Object.fromEntries(SHARED_MODULES.map((name) => [`shared-${name.replace(/[^a-z]+/g, '-').replace(/^-/, '')}`, PREFIX + name]));
      return { build: { rollupOptions: { input: { index: resolve(root, 'index.html'), ...entries }, preserveEntrySignatures: 'strict' } } };
    },
    resolveId(id) {
      return id.startsWith(PREFIX) ? id : undefined;
    },
    load(id) {
      return id.startsWith(PREFIX) ? reExport(id.slice(PREFIX.length)) : undefined;
    },
    transformIndexHtml: {
      order: 'post',
      handler(_, { bundle }) {
        if (!isBuild) return importMap((name) => `/@id/${(PREFIX + name).replace('\0', '__x00__')}`);

        const chunks = Object.values(bundle ?? {}).filter((output): output is Rollup.OutputChunk => output.type === 'chunk');
        return importMap((name) => {
          const chunk = chunks.find((each) => each.facadeModuleId === PREFIX + name);
          if (!chunk) throw new Error(`The build made no entry for the shared module ${name}.`);
          return `/${chunk.fileName}`;
        });
      },
    },
  };
}
