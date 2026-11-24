// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FileDetailsDialog } from './FileDetailsDialog';
import { saveTokens } from '../services/authService';
import type { FileMetadata, ShelfFile } from '../services/shelfService';
import { installFakeWebLocks } from '../test/fakeWebLocks';
import { inAnHour, json, stubServer } from '../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const photo: ShelfFile = {
  id: 'file-1', shelfId: 's', directoryId: 'root', path: '/photo.jpg', name: 'photo.jpg', contentType: 'image/jpeg', sizeBytes: 2048, sha256: 'ab'.repeat(32),
  createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z', description: null, encrypted: false,
};

describe('the file details dialog', () => {
  let root: Root;
  let sent: string[];
  let metadata: FileMetadata;
  let answerMetadata: () => Promise<Response>;

  async function show(file: ShelfFile): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(
      <QueryClientProvider client={client}>
        <FileDetailsDialog shelf="docs" file={file} onClose={() => {}} />
      </QueryClientProvider>,
    ));
  }

  beforeEach(() => {
    localStorage.clear();
    installFakeWebLocks();
    saveTokens({ token: 'first', expiresAt: inAnHour(), refreshToken: 'refresh-1' });
    metadata = {};
    answerMetadata = async () => json(metadata);
    sent = stubServer(async (request) => {
      const { pathname } = new URL(request.url);
      return pathname === '/api/shelves/docs/files/metadata' ? answerMetadata() : json({ message: 'Not here.' }, 404);
    });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
  });

  it('shows what the contents say beside the record', async () => {
    metadata = {
      readAt: '2026-11-01T00:01:00Z', takenAt: new Date(2024, 5, 1, 14, 30).toISOString(), latitude: 59.3293, longitude: -18.0686, camera: 'Apple iPhone 15', durationSeconds: null,
    };

    await show(photo);

    await vi.waitFor(() => expect(document.body.textContent).toContain('Apple iPhone 15'));
    const text = document.body.textContent;
    expect(text).toContain('Taken2024-06-01 14:30');
    expect(text).toContain('Place59.3293, -18.0686');
    expect(text).toContain('Size2 KiB');
    expect(text).not.toContain('Length');
    expect(sent).toContain('GET /api/shelves/docs/files/metadata?id=file-1 Bearer first');
  });

  it('says when the contents have not been read yet', async () => {
    metadata = { readable: true, readAt: null };

    await show(photo);

    await vi.waitFor(() => expect(document.body.textContent).toContain('Its contents have not been read yet.'));
  });

  it('says a file of a type the server does not read never will be', async () => {
    metadata = { readable: false, readAt: null };

    await show({ ...photo, path: '/notes.txt', name: 'notes.txt', contentType: 'text/plain' });

    await vi.waitFor(() => expect(document.body.textContent).toContain('The server does not read files of this type.'));
  });

  it('says what went wrong when the metadata cannot be loaded', async () => {
    answerMetadata = async () => json({ message: 'The disk is gone.' }, 500);

    await show(photo);

    await vi.waitFor(() => expect(document.body.textContent).toContain('Could not load what its contents say: The disk is gone.'));
    expect(document.body.textContent).toContain('Size2 KiB');
  });

  it('says it is loading until the server answers', async () => {
    answerMetadata = () => new Promise<Response>(() => {});

    await show(photo);

    expect(document.body.textContent).toContain('Loading what its contents say…');
  });

  it('shows the fields plugins declare under their names, and nothing of one that has none', async () => {
    metadata = {
      readable: false,
      plugins: {
        notes: {
          name: 'Notes',
          fields: [
            { name: 'caption', label: 'Caption', kind: 'text', document: 'file', value: { text: 'Two\nlines' } },
            { name: 'rating', label: 'Rating', kind: 'number', document: 'file', value: { number: 4.5 } },
            { name: 'due', label: 'Due', kind: 'date', document: 'file', value: { date: new Date(2026, 2, 1, 9, 5).toISOString() } },
            { name: 'done', label: 'Done', kind: 'flag', document: 'file', value: { flag: false } },
            { name: 'kept', label: 'Kept', kind: 'flag', document: 'file', value: { flag: true } },
          ],
        },
        empty: { name: 'Kept without fields', fileDocument: {}, fileVersion: 1, fields: [] },
      },
    };

    await show(photo);

    await vi.waitFor(() => expect(fieldsOf('Notes')).toEqual([
      ['Caption', 'Two\nlines'],
      ['Rating', '4.5'],
      ['Due', '2026-03-01 09:05'],
      ['Done', 'No'],
      ['Kept', 'Yes'],
    ]));
    expect(document.body.textContent).not.toContain('Kept without fields');
  });

  it('shows the fields of an encrypted file', async () => {
    metadata = { plugins: { notes: { name: 'Notes', fields: [{ name: 'rating', label: 'Rating', kind: 'number', document: 'file', value: { number: 0 } }] } } };

    await show({ ...photo, encrypted: true });

    await vi.waitFor(() => expect(fieldsOf('Notes')).toEqual([['Rating', '0']]));
  });

  it('says why it cannot show what plugins keep about an encrypted file', async () => {
    answerMetadata = async () => json({ message: 'The disk is gone.' }, 500);

    await show({ ...photo, encrypted: true });

    await vi.waitFor(() => expect(document.body.textContent).toContain('never reads its contents. Could not load what plugins keep about it: The disk is gone.'));
  });

  it('says an encrypted file is never read', async () => {
    await show({ ...photo, encrypted: true });

    expect(document.body.textContent).toContain('Encrypted: the server never reads its contents.');
  });
});

/** The labels and values in the section headed `heading`, which names it; undefined when there is none. */
function fieldsOf(heading: string): (string | null)[][] | undefined {
  const section = [...document.body.querySelectorAll('section')].find((each) => (
    document.getElementById(each.getAttribute('aria-labelledby') ?? '')?.textContent === heading
  ));
  return section && [...section.querySelectorAll('dt')].map((label) => [label.textContent, label.nextElementSibling!.textContent]);
}
