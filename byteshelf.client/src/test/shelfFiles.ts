import type { ShelfFile } from '../services/shelfService';

/** A small text file named `name`, its id and contents' hash too, with what is `changed`. */
export function shelfFile(name: string, changed: Partial<ShelfFile> = {}): ShelfFile {
  return {
    id: name, shelfId: 's', directoryId: 'd', path: `/${name}`, name, contentType: 'text/plain', sizeBytes: 5, sha256: name,
    createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z', description: null, encrypted: false, ...changed,
  };
}
