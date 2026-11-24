import { describe, expect, it } from 'vitest';
import { folderSuffixOf, folderTypeOf } from './folderTypes';
import type { FolderType } from './folderType';
import type { RunningPlugin } from './pluginHost';

function plugin(id: string, ...suffixes: string[]): RunningPlugin {
  return { id, viewers: [], fileActions: [], folderTypes: suffixes.map((suffix): FolderType => ({ suffix, label: `${suffix} of ${id}`, icon: () => null, component: () => null })) };
}

describe('a folder\'s suffix', () => {
  it.each([
    ['Trip.album', 'album'],
    ['Trip.ALBum', 'album'],
    ['A long trip.2026.album', 'album'],
    ['scene.3d', '3d'],
    ['..album', 'album'],
    ['.album', null],
    ['album', null],
    ['Trip.', null],
    ['v1.2', null],
    ['Trip.album.', null],
    ['Trip.album ', null],
    ['Trip.al-bum', null],
    // A letter that is lowercase k only once folded.
    ['Trip.booK', null],
    ['', null],
  ])('in %j is %j', (name, suffix) => {
    expect(folderSuffixOf(name)).toBe(suffix);
  });
});

describe('a folder\'s type', () => {
  const plugins = [plugin('a', 'album', 'scene'), plugin('b', 'album', 'model')];

  it.each([
    ['Trip.album', 'album of a'],
    ['Trip.ALBUM', 'album of a'],
    ['House.model', 'model of b'],
    ['House.other', undefined],
    ['.album', undefined],
    ['reports', undefined],
  ])('is the first plugin\'s that has its suffix: %s, %s', (name, label) => {
    expect(folderTypeOf(plugins, name)?.label).toBe(label);
  });
});
