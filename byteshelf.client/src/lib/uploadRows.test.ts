import { describe, expect, it } from 'vitest';
import { shelfFile } from '../test/shelfFiles';
import type { ShelfFile } from '../services/shelfService';
import { withUploads, type FileRow } from './uploadRows';
import type { ListedUpload } from './uploadStore';

/** When the listing was read, unless a test reads it later. */
const READ_AT = 1_000;

let lastId = 0;

function upload(name: string, { replacing = false, stored = null as ShelfFile | null, storedAt = READ_AT } = {}): ListedUpload {
  return {
    upload: { id: ++lastId, shelf: 'docs', folderPath: '/a', file: new File(['new'], name), name, path: `/a/${name}` },
    replacing,
    stored: stored && { file: stored, at: storedAt },
  };
}

/** Each row as its name, an upload's marked with an arrow, a file's with its id where that is not its name. */
function names(rows: FileRow[]): string[] {
  return rows.map(({ kind, value }) => (kind === 'upload' ? `↑${value.name}` : value.id === value.name ? value.name : `${value.name}#${value.id}`));
}

function listed(...fileNames: string[]): ShelfFile[] {
  return fileNames.map((name) => shelfFile(name));
}

function rowsOf(files: ShelfFile[], uploads: ListedUpload[], readAt = READ_AT): string[] {
  return names(withUploads({ files, readAt }, uploads).rows);
}

describe('a folder\'s files with the uploads into it', () => {
  it('puts each upload where its name comes among the files, as the listing orders them', () => {
    expect(rowsOf(listed('b.txt', 'd.txt'), [upload('e.txt'), upload('a.txt'), upload('c.txt')])).toEqual(['↑a.txt', 'b.txt', '↑c.txt', 'd.txt', '↑e.txt']);
  });

  it('puts one whose name is taken after the file of that name', () => {
    expect(rowsOf(listed('a.txt', 'b.txt'), [upload('a.txt')])).toEqual(['a.txt', '↑a.txt', 'b.txt']);
  });

  it('puts one replacing a file in its place', () => {
    expect(rowsOf(listed('a.txt', 'b.txt'), [upload('a.txt', { replacing: true })])).toEqual(['↑a.txt', 'b.txt']);
  });

  it('shows one done as the file it was stored as, in the place of a file of its name', () => {
    const stored = shelfFile('a.txt', { id: 'new' });

    expect(rowsOf(listed('a.txt', 'b.txt'), [upload('a.txt', { stored })])).toEqual(['a.txt#new', 'b.txt']);
  });

  it('releases one done once the listing has its file, which is then the listing\'s row', () => {
    const stored = shelfFile('a.txt', { id: 'new' });
    const done = upload('a.txt', { stored, storedAt: READ_AT + 1 });

    const { rows, released } = withUploads({ files: [stored, ...listed('b.txt')], readAt: READ_AT }, [done]);

    expect(names(rows)).toEqual(['a.txt#new', 'b.txt']);
    expect(released).toEqual([done]);
  });

  it('keeps one done while the listing was read before it was, and releases it once one read after lacks it', () => {
    const done = upload('a.txt', { stored: shelfFile('a.txt', { id: 'new' }), storedAt: READ_AT });

    expect(withUploads({ files: [], readAt: READ_AT }, [done]).released).toEqual([]);
    const later = withUploads({ files: [], readAt: READ_AT + 1 }, [done]);
    expect(later).toEqual({ rows: [], released: [done] });
  });
});
