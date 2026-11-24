import { describe, expect, it } from 'vitest';
import type { ShelfFile } from '../../../services/shelfService';
import { BUILT_IN_VIEWERS } from '.';

function viewerOf(contentType: string, name = 'a.bin'): string | null {
  return BUILT_IN_VIEWERS.viewerFor({ name, contentType } as ShelfFile)?.viewer?.id ?? null;
}

describe('the built-in viewers', () => {
  it.each([
    ['image/png', 'image'],
    ['image/svg+xml', 'image'],
    ['image/heic', 'image'],
    ['video/mp4', 'video'],
    ['audio/mpeg', 'audio'],
    ['application/pdf', 'pdf'],
    ['text/plain; charset=utf-8', 'text'],
    ['text/html', 'text'],
    ['text/markdown', 'text'],
    ['application/json', 'text'],
    ['application/xml', 'text'],
    ['application/xhtml+xml', 'text'],
    ['application/javascript', 'text'],
    ['application/yaml', 'text'],
    ['application/zip', null],
    ['application/vnd.openxmlformats-officedocument.wordprocessingml.document', null],
  ])('show %s as %s', (contentType, viewer) => {
    expect(viewerOf(contentType)).toBe(viewer);
  });

  // A browser shows an SVG or a PDF only by its type, which an untyped one does not have.
  it.each([
    ['a.PNG', 'image'],
    ['a.heic', 'image'],
    ['notes.md', 'text'],
    ['main.rs', 'text'],
    ['a.mp4', 'video'],
    ['a.mp3', 'audio'],
    ['a.svg', null],
    ['a.pdf', null],
    ['a.bin', null],
  ])('show an untyped %s as %s', (name, viewer) => {
    expect(viewerOf('application/octet-stream', name)).toBe(viewer);
  });
});
