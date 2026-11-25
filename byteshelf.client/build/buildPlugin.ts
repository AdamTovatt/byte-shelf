// Checks the types of the plugin in the working directory and builds it, with its server part if it has one: `node ../../byteshelf.client/build/buildPlugin.ts`.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { build } from 'vite';
import { pluginConfig } from './pluginConfig.ts';

const folder = process.cwd();
const tsc = createRequire(import.meta.url).resolve('typescript/bin/tsc');

run(process.execPath, [tsc, '--noEmit', '-p', folder]);

await build(pluginConfig(folder));

const manifest = JSON.parse(readFileSync(join(folder, 'plugin.json'), 'utf8')) as { serverAssembly?: string };
if (manifest.serverAssembly) {
  // For no one runtime, so one plugin folder is for every platform. After the build above, which empties dist.
  run('dotnet', ['publish', 'server', '--configuration', 'Release', '--output', 'dist']);
}

function run(command: string, args: string[]): void {
  try {
    execFileSync(command, args, { cwd: folder, stdio: 'inherit' });
  } catch {
    // What is wrong it has written itself.
    process.exit(1);
  }
}
