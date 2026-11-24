import { describe, expect, it } from 'vitest';
import type { ShelfFile } from '../../services/shelfService';
import { ViewerRegistry, type Viewer } from './viewerRegistry';

function viewer(id: string, mediaTypes: string[], extensions: string[] = []): Viewer {
  return { id, mediaTypes, extensions, component: () => null };
}

function file(contentType: string, name = 'a.bin'): ShelfFile {
  return { id: 'f', shelfId: 's', directoryId: 'd', path: `/${name}`, name, contentType, sizeBytes: 1, sha256: '', createdAt: '', updatedAt: '', description: null, encrypted: false };
}

/** The viewer picked among those registered, by each one's plugin. */
function picked(registered: [string | null, Viewer][], shown: ShelfFile): string | null {
  const registry = new ViewerRegistry();
  for (const [plugin, each] of registered) registry.register(plugin, [each]);
  return registry.viewerFor(shown)?.id ?? null;
}

describe('the viewer a file is shown with', () => {
  it('takes its type, or its type\'s family, without parameters and whatever the case', () => {
    const registered: [null, Viewer][] = [[null, viewer('pdf', ['application/pdf'])], [null, viewer('image', ['image/*'])]];

    expect(picked(registered, file('Application/PDF; version=1'))).toBe('pdf');
    expect(picked(registered, file('image/png'))).toBe('image');
    expect(picked(registered, file('imagery/png'))).toBeNull();
    expect(picked(registered, file('application/zip'))).toBeNull();
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
});
