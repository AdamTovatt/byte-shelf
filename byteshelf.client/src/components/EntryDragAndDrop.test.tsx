// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { RunningPlugin } from '../lib/plugins/pluginHost';
import { directoryEntry, type Entry } from '../services/shelfService';
import { shelfDirectory, shelfFile } from '../test/shelfFiles';
import { EntryDragPreview } from './EntryDragAndDrop';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../hooks/usePluginHost', () => {
  const plugin: RunningPlugin = { id: 'a', viewers: [], fileActions: [], folderTypes: [{ suffix: 'album', label: 'Album', icon: () => <svg data-icon="album" />, component: () => null }] };
  return { usePluginHost: () => ({ plugins: [plugin] }) };
});

describe('what follows the pointer as entries are dragged', () => {
  let root: Root;

  beforeEach(() => {
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  function folder(path: string): Entry {
    return directoryEntry({ directory: shelfDirectory(path), sizeBytes: 0, fileCount: 0, encryptedFileCount: 0 });
  }

  const album = folder('/Trip.album');

  it.each<[string, Entry[], boolean]>([
    ['one folder of a type', [album], true],
    ['one plain folder', [folder('/reports')], false],
    ['one file', [{ kind: 'file', value: shelfFile('Trip.album') }], false],
    ['several', [album, folder('/reports')], false],
  ])('has the icon of a type for a folder of it alone: %s', (_, entries, has) => {
    act(() => root.render(<EntryDragPreview entries={entries} />));

    expect(document.body.querySelector('[data-icon="album"]') !== null).toBe(has);
  });
});
