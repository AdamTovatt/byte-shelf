import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { createServer as createHttpServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { resolve } from 'node:path';
import ts from 'typescript';
import { build, createServer, type Rollup } from 'vite';
import { beforeEach, describe, expect, it } from 'vitest';
import { SHARED_MODULES, sharedModules } from './sharedModules';
import { BUILD_MS, useTestFolder } from './testFolder';

function importsIn(page: string): Record<string, string> {
  return (JSON.parse(/<script type="importmap">([^<]+)<\/script>/.exec(page)![1]) as { imports: Record<string, string> }).imports;
}

describe('the modules shared with plugins', () => {
  const folder = useTestFolder('app');

  async function buildApp(minify: boolean): Promise<Rollup.RollupOutput['output']> {
    return (await build({ root: folder.path, configFile: false, logLevel: 'silent', build: { write: false, minify }, plugins: [sharedModules()] }) as Rollup.RollupOutput).output;
  }

  beforeEach(() => {
    folder.write({
      'index.html': '<html><head></head><body><script type="module" src="/src/main.ts"></script></body></html>',
      'src/main.ts': `
        import { useState } from 'react';
        import { useQuery } from '@tanstack/react-query';
        import { Link } from 'react-router-dom';
        import './plugin/index.ts';
        console.log(useState, useQuery, Link);`,
      'src/plugin/index.ts': 'export const fromTheApp = 1;',
    });
  });

  it('each have an entry the import map names, exporting what the module does', async () => {
    const output = await buildApp(true);

    const page = String((output.find((each) => each.fileName === 'index.html') as Rollup.OutputAsset).source);
    const imports = importsIn(page);
    expect(Object.keys(imports)).toEqual([...SHARED_MODULES]);
    expect(page.indexOf('importmap')).toBeLessThan(page.indexOf('type="module"'));

    const exportsOf = (name: string) => (output.find((each) => `/${each.fileName}` === imports[name]) as Rollup.OutputChunk).exports;
    expect(exportsOf('react')).toEqual(expect.arrayContaining(['default', 'useState', 'createContext', 'lazy']));
    expect(exportsOf('react/jsx-runtime')).toEqual(expect.arrayContaining(['jsx', 'jsxs', 'Fragment']));
    expect(exportsOf('react-dom')).toEqual(expect.arrayContaining(['default', 'createPortal', 'flushSync']));
    expect(exportsOf('@tanstack/react-query')).toEqual(expect.arrayContaining(['useQuery', 'useQueryClient', 'QueryClient']));
    expect(exportsOf('react-router-dom')).toEqual(expect.arrayContaining(['useNavigate', 'Link']));
    expect(exportsOf('@byteshelf/plugin')).toEqual(['fromTheApp']);
  }, BUILD_MS);

  it('are one copy, the app\'s own', async () => {
    const output = await buildApp(false);

    const holding = (module: RegExp) => output.filter((each): each is Rollup.OutputChunk => each.type === 'chunk' && Object.keys(each.modules).some((id) => module.test(id)));
    expect(holding(/\/react\/cjs\/react\.production/)).toHaveLength(1);
    expect(holding(/\/@tanstack\/react-query\/.*\/QueryClientProvider\./)).toHaveLength(1);
    expect(holding(/\/react-router\/dist\//)).toHaveLength(1);
    expect(holding(/\/src\/plugin\/index\.ts$/)).toHaveLength(1);
  }, BUILD_MS);

  it('are each served by the dev server where the import map says', async () => {
    // The packages as they are: only where they are served is of interest, not their being bundled first.
    const server = await createServer({ root: folder.path, configFile: false, logLevel: 'silent', server: { middlewareMode: true, ws: false }, optimizeDeps: { noDiscovery: true }, plugins: [sharedModules()] });
    const http = createHttpServer(server.middlewares).listen(0, '127.0.0.1');
    try {
      await once(http, 'listening');
      const imports = importsIn(await server.transformIndexHtml('/', folder.read('index.html')));
      expect(Object.keys(imports)).toEqual([...SHARED_MODULES]);

      const served = await Promise.all(Object.values(imports).map(async (address) => {
        const response = await fetch(`http://127.0.0.1:${(http.address() as AddressInfo).port}${address}`);
        expect([address, response.status]).toEqual([address, 200]);
        return response.text();
      }));
      const [react, jsxRuntime, reactDom, query, router, plugin] = served;
      expect(react).toContain('useState');
      expect(jsxRuntime).toContain('jsxs');
      expect(reactDom).toContain('createPortal');
      expect(query).toMatch(/export \* from .*react-query/);
      expect(router).toMatch(/export \* from .*react-router-dom/);
      expect(plugin).toMatch(/export \* from .*\/src\/plugin\/index\.ts/);
    } finally {
      http.close();
      http.closeAllConnections();
      await server.close();
    }
  }, BUILD_MS);

  it('each have their types in what a plugin\'s tsconfig extends', () => {
    const { config } = ts.readConfigFile(resolve('../plugins/tsconfig.base.json'), (path) => readFileSync(path, 'utf8')) as { config: { compilerOptions: { paths: Record<string, string[]> } } };
    const patterns = Object.keys(config.compilerOptions.paths).map((pattern) => new RegExp(`^${pattern.replace('*', '.+')}$`));

    expect(SHARED_MODULES.filter((name) => !patterns.some((pattern) => pattern.test(name)))).toEqual([]);
  });
});
