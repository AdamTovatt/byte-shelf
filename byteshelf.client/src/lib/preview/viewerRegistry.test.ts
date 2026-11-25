import { describe, expect, it, vi } from 'vitest';
import type { ShelfFile } from '../../services/shelfService';
import { shelfFile } from '../../test/shelfFiles';
import { ViewerRegistry, type Viewer } from './viewerRegistry';

function viewer(id: string, mediaTypes: string[], extensions: string[] = []): Viewer {
  return { id, mediaTypes, extensions, component: () => null };
}

function file(contentType: string, name = 'a.bin'): ShelfFile {
  return shelfFile(name, { contentType });
}

/** The viewer picked among those registered, by each one's plugin. */
function picked(registered: [string | null, Viewer][], shown: ShelfFile): string | null {
  const registry = registered.reduce((before, [plugin, each]) => before.with(plugin, [each]), ViewerRegistry.EMPTY);
  return registry.viewerFor(shown)?.viewers[0]?.viewer.id ?? null;
}

describe('the viewer a file is shown with', () => {
  it('takes its type, or its type\'s family, without parameters and whatever the case', () => {
    const registered: [null, Viewer][] = [[null, viewer('pdf', ['application/pdf'])], [null, viewer('image', ['image/*'])]];

    expect(picked(registered, file('Application/PDF; version=1'))).toBe('pdf');
    expect(picked(registered, file('image/png'))).toBe('image');
    expect(picked(registered, file('imagery/png'))).toBeNull();
    expect(picked(registered, file('application/zip'))).toBeNull();
  });

  it('compares a plugin\'s types and extensions whatever their case', () => {
    expect(picked([['a', viewer('raw', ['Image/X-Raw'], ['.MD'])]], file('image/x-raw'))).toBe('raw');
    expect(picked([['a', viewer('raw', [], ['.MD'])]], file('application/octet-stream', 'README.md'))).toBe('raw');
  });

  it('is picked by a registry that holds more, while the one it came from stays as it was', () => {
    const before = ViewerRegistry.EMPTY;
    const after = before.with(null, [viewer('image', ['image/*'])]);

    expect(after.viewerFor(file('image/png'))?.viewers[0]?.viewer.id).toBe('image');
    expect(before.viewerFor(file('image/png'))).toBeNull();
  });

  it('is found by the name\'s extension only for a file stored without a type', () => {
    const registered: [null, Viewer][] = [[null, viewer('text', ['text/*'], ['.md'])]];

    expect(picked(registered, file('application/octet-stream', 'README.MD'))).toBe('text');
    expect(picked(registered, file('application/zip', 'README.md'))).toBeNull();
    expect(picked(registered, file('application/octet-stream', 'md'))).toBeNull();
  });

  it('is one of its exact type before one of its family, a built-in one before a plugin\'s included', () => {
    expect(picked([['a', viewer('family', ['image/*'])], [null, viewer('exact', ['image/png'])]], file('image/png'))).toBe('exact');
  });

  it('is a plugin\'s before a built-in one, and the first plugin\'s by id, whatever the order they come in', () => {
    const registered: [string | null, Viewer][] = [
      [null, viewer('built-in', ['image/*'])],
      ['zebra', viewer('zebra', ['image/*'])],
      ['ant', viewer('ant', ['image/*'])],
    ];

    expect(picked(registered, file('image/png'))).toBe('ant');
    expect(picked(registered.slice(0, 2), file('image/png'))).toBe('zebra');
    expect(picked(registered.slice(0, 1), file('image/png'))).toBe('built-in');
  });

  it('is the next that takes the file when one is unavailable, keeping why the first is', () => {
    const unavailable = (id: string) => ({ ...viewer(id, ['application/pdf']), unavailable: () => `No ${id} here.` });
    const available = { ...viewer('available', ['application/pdf']), unavailable: () => null };
    const registry = ViewerRegistry.EMPTY.with('ant', [unavailable('ant')]).with(null, [unavailable('built-in')]);
    const pdf = file('application/pdf');

    expect(registry.viewerFor(pdf)).toEqual({ viewers: [], unavailable: 'No ant here.' });
    expect(registry.with('bee', [available]).viewerFor(pdf)).toEqual({ viewers: [{ viewer: available, pluginId: 'bee' }], unavailable: 'No ant here.' });
    expect(registry.with(null, [available]).viewerFor(pdf)).toEqual({ viewers: [{ viewer: available, pluginId: null }], unavailable: 'No ant here.' });
    expect(registry.viewerFor(file('image/png'))).toBeNull();
  });

  it('are those that would show the file after the first, in order, but those that are unavailable, whose reason it keeps', () => {
    const unavailable = { ...viewer('gone', ['image/*']), unavailable: () => 'Not here.' };
    const registry = ViewerRegistry.EMPTY
      .with(null, [viewer('built-in', ['image/*'])])
      .with('zebra', [viewer('zebra', ['image/*'])])
      .with('bee', [unavailable])
      .with('ant', [viewer('ant', ['image/*']), viewer('exact', ['image/png'])]);

    const match = registry.viewerFor(file('image/png'));

    expect(match?.viewers.map(({ viewer: { id }, pluginId }) => `${pluginId} ${id}`)).toEqual(['ant exact', 'ant ant', 'zebra zebra', 'null built-in']);
    expect(match?.unavailable).toBe('Not here.');
  });

  it('passes over one that fails to say whether it is available, logging why once', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failing: Viewer = { ...viewer('failing', ['text/plain']), unavailable: () => { throw new Error('It threw.'); } };

    expect(picked([['a', failing], [null, viewer('text', ['text/*'])]], file('text/plain'))).toBe('text');
    expect(ViewerRegistry.EMPTY.with('a', [failing]).viewerFor(file('text/plain'))).toEqual({ viewers: [], unavailable: 'The viewer failed.' });
    expect(error.mock.calls).toEqual([['The viewer failing could not say whether it is available.', new Error('It threw.')]]);
    error.mockRestore();
  });
});
