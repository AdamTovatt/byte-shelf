import { describe, expect, it } from 'vitest';
import { shelfDirectory, shelfFile } from '../test/shelfFiles';
import type { DirectoryListing } from '../services/shelfService';
import { folderRows, type FolderRow } from './uploadRows';
import type { ListedUpload } from './uploadStore';

let lastId = 0;

function upload(name: string, replacing = false): ListedUpload {
  return { upload: { id: ++lastId, shelf: 'docs', folderPath: '/a', file: new File(['new'], name), name, path: `/a/${name}` }, replacing };
}

/** Each row as its name, a folder's ending in a slash and an upload's marked with an arrow. */
function names(rows: FolderRow[]): string[] {
  return rows.map(({ kind, value }) => (kind === 'upload' ? `↑${value.name}` : kind === 'directory' ? `${value.name}/` : value.name));
}

/** The listing of /a with these files, in this order, and these folders. */
function listed(fileNames: string[], folderNames: string[] = []): DirectoryListing {
  return {
    directory: shelfDirectory('/a'),
    directories: folderNames.map((name) => ({ directory: shelfDirectory(`/a/${name}`), sizeBytes: 0, fileCount: 0, encryptedFileCount: 0 })),
    files: fileNames.map((name) => shelfFile(name)),
  };
}

describe('a folder\'s rows with the uploads into it', () => {
  it('puts each upload where its name comes among the files', () => {
    expect(names(folderRows(listed(['b.txt', 'd.txt']), [upload('e.txt'), upload('a.txt'), upload('c.txt')]))).toEqual(['↑a.txt', 'b.txt', '↑c.txt', 'd.txt', '↑e.txt']);
  });

  it('orders the folders, and the files, by name itself, whatever order the listing has them in', () => {
    expect(names(folderRows(listed(['d.txt', 'B.txt', 'a.txt'], ['z', 'C', 'b']), [upload('c.txt')]))).toEqual(['C/', 'b/', 'z/', 'B.txt', 'a.txt', '↑c.txt', 'd.txt']);
  });

  it('puts one whose name is taken after the file of that name', () => {
    expect(names(folderRows(listed(['b.txt', 'a.txt']), [upload('a.txt')]))).toEqual(['a.txt', '↑a.txt', 'b.txt']);
  });

  it('puts one replacing a file in its place', () => {
    expect(names(folderRows(listed(['a.txt', 'b.txt']), [upload('a.txt', true)]))).toEqual(['↑a.txt', 'b.txt']);
  });
});
