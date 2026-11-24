import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { build } from 'vite';
import { beforeEach, describe, expect, it } from 'vitest';
import { pluginConfig } from './pluginConfig';
import { SHARED_MODULES } from './sharedModules';
import { BUILD_MS, useTestFolder } from './testFolder';

describe('building a plugin', () => {
  const folder = useTestFolder('probe');

  async function buildPlugin(): Promise<void> {
    await build({ logLevel: 'silent', ...pluginConfig(folder.path) });
  }

  beforeEach(() => {
    folder.write({
      'plugin.json': '{ "id": "probe", "name": "Probe", "api": 1 }',
      'src/plugin.css': '@tailwind components; @tailwind utilities;',
      'src/plugin.tsx': `
        import { lazy, useState } from 'react';
        import { createPortal } from 'react-dom';
        import { useQuery } from '@tanstack/react-query';
        import { useNavigate } from 'react-router-dom';
        import { Card } from '@byteshelf/plugin';
        import './plugin.css';
        const Heavy = lazy(() => import('./Heavy'));
        export default { used: [useState, createPortal, useQuery, useNavigate], draw: () => <Card className="probe-flex 2xl:probe-grid-cols-[1fr,2fr]"><Heavy /></Card> };`,
      'src/Heavy.tsx': `
        import './heavy.css';
        export const start = () => new Worker(new URL('./count.ts', import.meta.url), { type: 'module' });
        export default function Heavy() { return <b className="probe-heavy">{new URL('./data.bin', import.meta.url).href}</b>; }`,
      'src/count.ts': 'postMessage(1);',
      'src/heavy.css': '.probe-heavy { color: red }',
      'src/data.bin': 'x'.repeat(8192),
    });
  });

  it('makes a plugin folder that shares the app\'s modules and knows no address of its own', async () => {
    await buildPlugin();

    const version = /<Version>(.+)<\/Version>/.exec(readFileSync(resolve('../ByteShelf.Server/ByteShelf.Server.csproj'), 'utf8'))![1];
    expect(JSON.parse(folder.read('dist/plugin.json'))).toEqual({ id: 'probe', name: 'Probe', api: 1, version });

    const module = folder.read('dist/ui/plugin.js');
    for (const shared of SHARED_MODULES) expect(module).toContain(`from"${shared}"`);
    expect(module).toMatch(/import\("\.\/Heavy-[\w-]+\.js"\)/);

    const styles = folder.read('dist/ui/plugin.css');
    expect(styles).toContain('.probe-flex{');
    expect(styles).toContain('.probe-heavy{');
    expect(styles).toContain('probe-grid-cols-');

    // What the server serves: all of it but the manifest, flat in ui.
    expect(readdirSync(join(folder.path, 'dist')).sort()).toEqual(['plugin.json', 'ui']);
    const files = readdirSync(join(folder.path, 'dist/ui'));
    expect(files.filter((file) => file.endsWith('.css'))).toEqual(['plugin.css']);
    expect(files.map((file) => file.replace(/-[\w-]{8}\./, '-*.')).sort()).toEqual(['Heavy-*.js', 'count-*.js', 'data-*.bin', 'plugin.css', 'plugin.js']);

    const heavy = folder.read(`dist/ui/${files.find((file) => file.startsWith('Heavy-'))}`);
    const addressed = [...heavy.matchAll(/new URL\(\s*["'`]([^"'`]+)["'`]/g)].map(([, address]) => address);
    expect(addressed).toHaveLength(2);
    for (const address of addressed) expect(existsSync(join(folder.path, 'dist/ui', address)), address).toBe(true);
  }, BUILD_MS);

  it('is what buildPlugin.ts does in the plugin\'s folder, once its types are right', () => {
    const run = () => spawnSync(process.execPath, [resolve('build/buildPlugin.ts')], { cwd: folder.path, encoding: 'utf8' });
    folder.write({
      'tsconfig.json': `{ "extends": "${resolve('../plugins/tsconfig.base.json')}", "include": ["src"], "exclude": [] }`,
      'src/plugin.tsx': 'import { definePlugin, Card } from \'@byteshelf/plugin\'; export default definePlugin({ viewers: [{ id: \'probe\', mediaTypes: [\'text/plain\'], extensions: [], component: ({ file }) => <Card className="probe-flex">{file.name}</Card> }] });',
    });

    const { status, stderr, stdout } = run();
    expect([status, stderr], stdout).toEqual([0, '']);
    expect(folder.read('dist/ui/plugin.js')).toContain('from"@byteshelf/plugin"');

    folder.write({ 'src/Heavy.tsx': 'export const wrong: number = \'text\';' });
    const refused = run();
    expect(refused.status).toBe(1);
    expect(refused.stdout).toContain('src/Heavy.tsx');
  }, BUILD_MS);

  it('fails on a utility without the plugin\'s prefix, in its source', async () => {
    folder.write({ 'src/Heavy.tsx': 'export default function Heavy() { return <b className="probe-flex 2xl:hidden" />; }' });

    await expect(buildPlugin()).rejects.toThrow(/Utilities without the plugin's class prefix: 2xl:hidden \(.*Heavy\.tsx\)/);
  }, BUILD_MS);

  it('fails on a utility without the plugin\'s prefix, in a stylesheet', async () => {
    folder.write({ 'src/heavy.css': '.probe-heavy .flex { color: red }' });

    await expect(buildPlugin()).rejects.toThrow('Utilities without the plugin\'s class prefix: flex (its stylesheet)');
  }, BUILD_MS);

  it.each(['react-dom/client', 'react/jsx-dev-runtime', '@tanstack/react-query/build/modern/index.js', 'react-router'])('fails on %s, which would bring a second copy of what is shared', async (name) => {
    folder.write({ 'src/Heavy.tsx': `import * as all from '${name}'; export default () => <b>{Object.keys(all)}</b>;` });

    await expect(buildPlugin()).rejects.toThrow(`${name}, which`);
  }, BUILD_MS);

  it('refuses an id that is not its folder\'s name', () => {
    folder.write({ 'plugin.json': '{ "id": "other", "name": "Probe", "api": 1 }' });

    expect(() => pluginConfig(folder.path)).toThrow('plugin.json has the id other; it must be its folder\'s name, probe.');
  });

  it.each([
    ['linux-x64', true],
    ['images-linux-arm64', true],
    ['win', true],
    ['windows', false],
    ['linuxfonts', false],
    ['images-linux', true],
    ['linux-x64-fonts', true],
  ])('says whether %s reads as a server\'s zip when released', (id, isRefused) => {
    const home = join('..', id);
    folder.write({ [`${home}/plugin.json`]: `{ "id": "${id}", "name": "Probe", "api": 1 }` });

    const config = () => pluginConfig(join(folder.path, home));
    if (isRefused) expect(config).toThrow(`The plugin ${id} would be released under a name that reads as a server's zip.`);
    else expect(config).not.toThrow();
  });
});
