import type { EncryptedFolder } from '../services/encryptionService';
import type { ShelfFile } from '../services/shelfService';
import { createCheck, encryptBlob } from '../lib/encryption/age';
import { FAST } from './encryptedShelf';
import { json } from './fakeServer';
import { shelfDirectory, shelfFile } from './shelfFiles';

/** A file as it was sent to be stored. */
interface SentFile {
  path: string;
  overwrite: boolean;
  encrypted: boolean;
  type: string | null;
  contents: Blob;
}

/** A shelf named docs with one folder, as the server answers for it. */
export function uploadShelf(path: string) {
  const directory = shelfDirectory(path);
  const files = new Map<string, { file: ShelfFile; contents: Blob }>();

  const shelf = {
    directory,
    /** The encrypted folder it is, or is in. */
    encryptedBy: null as EncryptedFolder | null,
    /** The files sent to it, refused ones too. */
    sent: [] as SentFile[],
    /** Why it refuses what is sent, as a conflict. */
    refusal: null as string | null,
    /** Whether a request fails. */
    fails: (() => false) as (request: Request) => boolean,

    async encrypt(password: string): Promise<EncryptedFolder> {
      shelf.encryptedBy = { directory, check: await createCheck(password, FAST) };
      return shelf.encryptedBy;
    },

    /** Puts a file there, encrypted with `password` when one is given. */
    async put(name: string, password?: string): Promise<ShelfFile> {
      const at = `${path}/${name}`;
      const plain = new Blob(['old']);
      const file = shelfFile(name, { path: at, id: `old:${name}`, encrypted: password !== undefined });
      files.set(at, { file, contents: password === undefined ? plain : await encryptBlob(plain, password, '', FAST) });
      return file;
    },

    /** The file there now. */
    file(name: string): ShelfFile {
      return files.get(`${path}/${name}`)!.file;
    },

    /** Has another folder in the folder's place, as once it has been moved and one made anew. */
    replaceFolder(): void {
      shelf.directory = shelfDirectory(path, { id: 'another' });
    },

    async answer(request: Request): Promise<Response> {
      if (shelf.fails(request)) return json({ message: 'It is down.' }, 500);
      const { pathname, searchParams: query } = new URL(request.url);
      switch (`${request.method} ${pathname}`) {
        case 'GET /api/shelves/docs/directories/encryption':
          return json({ encryptedBy: shelf.encryptedBy });
        case 'GET /api/shelves/docs/tree':
          return json({ directory: shelf.directory, directories: [], files: [] });
        case 'GET /api/shelves/docs/entries':
          return json({ directory: shelf.directory, encryptedBy: shelf.encryptedBy, directories: [], files: Array.from(files.values(), ({ file }) => file) });
        case 'GET /api/shelves/docs/files': {
          const found = files.get(query.get('path')!);
          return found ? json(found.file) : json({ message: 'There is no such file.', code: 'not_found' }, 404);
        }
        case 'GET /api/shelves/docs/content':
          return new Response(Array.from(files.values()).find(({ file }) => file.id === query.get('id'))!.contents);
        case 'PUT /api/shelves/docs/content':
          return store(query, request);
        default:
          return json({ message: 'Not here.', code: 'not_found' }, 404);
      }
    },
  };

  async function store(query: URLSearchParams, request: Request): Promise<Response> {
    const at = query.get('path')!;
    const overwrite = query.get('overwrite') === 'true';
    const encrypted = query.get('encrypted') === 'true';
    const contents = new Blob([await request.arrayBuffer()]);
    shelf.sent.push({ path: at, overwrite, encrypted, type: request.headers.get('Content-Type'), contents });
    if (shelf.refusal !== null) return json({ message: shelf.refusal, code: 'conflict' }, 409);
    if (shelf.encryptedBy && !encrypted) return json({ message: 'The folder is encrypted.', code: 'conflict' }, 409);
    if (files.has(at) && !overwrite) return json({ message: `A file already exists at '${at}'.`, code: 'conflict' }, 409);
    const file = shelfFile(at.slice(path.length + 1), { path: at, id: `new${shelf.sent.length}:${at}`, encrypted });
    files.set(at, { file, contents });
    return json(file);
  }

  return shelf;
}
