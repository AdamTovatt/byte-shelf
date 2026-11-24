// Checks the types of the plugin in the working directory and builds it: `node ../../byteshelf.client/build/buildPlugin.ts`.
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { build } from 'vite';
import { pluginConfig } from './pluginConfig.ts';

const folder = process.cwd();
const tsc = createRequire(import.meta.url).resolve('typescript/bin/tsc');

try {
  execFileSync(process.execPath, [tsc, '--noEmit', '-p', folder], { stdio: 'inherit' });
} catch {
  // What is wrong it has written itself.
  process.exit(1);
}

await build(pluginConfig(folder));
