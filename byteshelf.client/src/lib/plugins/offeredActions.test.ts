import { afterEach, describe, expect, it, vi } from 'vitest';
import { fileActionsFor } from './offeredActions';
import type { RunningPlugin } from './pluginHost';
import type { FileAction } from './fileAction';
import type { ShelfFile } from '../../services/shelfService';

function action(id: string, showsFor: FileAction['showsFor']): FileAction {
  return { id, label: id, showsFor, run: () => undefined };
}

function plugin(id: string, fileActions: FileAction[]): RunningPlugin {
  return { id, viewers: [], fileActions };
}

describe('a file\'s actions', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('are those every plugin has for it, in order', () => {
    const plugins: RunningPlugin[] = [
      plugin('a', [action('second', () => true), action('first', () => true)]),
      plugin('b', []),
      plugin('c', [action('text', ({ contentType }) => contentType === 'text/plain'), action('images', ({ contentType }) => contentType === 'image/png'), action('first', () => true)]),
    ];

    expect(fileActionsFor(plugins, { contentType: 'text/plain' } as ShelfFile).map(({ pluginId, key, action: { id } }) => [pluginId, key, id])).toEqual([['a', 'a second', 'second'], ['a', 'a first', 'first'], ['c', 'c text', 'text'], ['c', 'c first', 'first']]);
  });

  it('leave out one that fails, saying which once, however often they are asked for', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failing = action('failing', () => { throw new Error('It threw.'); });

    const plugins = [plugin('a', [failing, action('working', () => true)])];

    for (let asked = 0; asked < 2; asked++) expect(fileActionsFor(plugins, {} as ShelfFile).map(({ action: { id } }) => id)).toEqual(['working']);
    expect(error.mock.calls).toEqual([['The action failing of the plugin a could not be asked whether it shows.', new Error('It threw.')]]);
  });
});
