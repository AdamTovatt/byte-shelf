// Runs the tests of the plugin in the working directory: `node ../../byteshelf.client/build/testPlugin.ts`.
import { resolve } from 'node:path';
import { startVitest } from 'vitest/node';

// It sets the process's exit code as tests fail.
const vitest = await startVitest('test', process.argv.slice(2), { run: true, config: resolve(import.meta.dirname, 'pluginTests.config.ts') });
await vitest.close();
