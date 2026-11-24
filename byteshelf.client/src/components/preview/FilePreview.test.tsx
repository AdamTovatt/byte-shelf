// @vitest-environment happy-dom
import { act, useEffect, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FilePreview } from './FilePreview';
import { viewers } from './viewers';
import { neighboursOf } from '../../lib/preview/neighbours';
import type { ViewerProps } from '../../lib/preview/viewerRegistry';
import type { ContentSource } from '../../services/contentService';
import type { ShelfFile } from '../../services/shelfService';
import { press } from '../../test/dom';
import { json, stubServer } from '../../test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

/** The files saved here, by the name a blob was saved under or the address one was saved from. */
const saved = vi.hoisted(() => [] as string[]);

vi.mock('../../lib/localBlobs', async (original) => ({
  ...(await original<typeof import('../../lib/localBlobs')>()),
  saveBlob: (_blob: Blob, name: string) => void saved.push(name),
  saveFrom: (href: string) => void saved.push(href),
}));

/** Throws while rendering, as a broken viewer does. */
function BrokenViewer({ file }: ViewerProps): null {
  throw new Error(`Cannot show ${file.name}.`);
}

viewers.register(null, [{ id: 'broken', mediaTypes: ['x-test/broken'], extensions: [], component: BrokenViewer }]);

function file(name: string, changed: Partial<ShelfFile> = {}): ShelfFile {
  return {
    id: name, shelfId: 's', directoryId: 'd', path: `/${name}`, name, contentType: 'text/plain', sizeBytes: 5, sha256: name,
    createdAt: '2026-11-02T00:00:00Z', updatedAt: '2026-11-02T00:00:00Z', description: null, encrypted: false, ...changed,
  };
}

const source: ContentSource = {
  navigationUrl: async (id, download) => `/open/${id}${download ? '?download' : ''}`,
  read: async () => new Blob(['whole']),
  readRange: async (id) => new Blob([`text of ${id}`]),
  encryptedFolderOf: async () => null,
};

describe('the preview', () => {
  let root: Root;
  let contents: (file: ShelfFile, signal?: AbortSignal) => Promise<Blob | null>;
  let closed: number;

  /** Shows another record of the file previewed, as the page does once the shelf has changed. */
  let change: (file: ShelfFile) => void;
  let moreFound: (() => void) | undefined;
  let detailsOn: string | undefined;

  function Page({ files, first }: { files: ShelfFile[]; first: ShelfFile }) {
    const [shown, setShown] = useState(first);
    useEffect(() => {
      change = setShown;
    }, []);
    return (
      <FilePreview
        file={shown}
        error={null}
        reading={{ source, contents: (each, signal) => contents(each, signal) }}
        neighbours={neighboursOf(files, shown.id)}
        onMoveTo={setShown}
        onMoveFurther={moreFound}
        onClose={() => closed++}
        detailsOn={detailsOn}
      />
    );
  }

  async function show(first: ShelfFile, files: ShelfFile[] = []): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<QueryClientProvider client={client}><Page files={files} first={first} /></QueryClientProvider>));
  }

  async function key(name: string, target: EventTarget = document, init: KeyboardEventInit = {}): Promise<void> {
    await act(async () => void target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init })));
  }

  function shows(text: string): Promise<void> {
    return vi.waitFor(async () => {
      await act(async () => {});
      expect(document.body.textContent).toContain(text);
    });
  }

  function labelled(label: string): HTMLButtonElement | null {
    return document.querySelector(`button[aria-label="${label}"]`);
  }

  beforeEach(() => {
    vi.stubGlobal('isSecureContext', true);
    saved.length = 0;
    closed = 0;
    moreFound = undefined;
    detailsOn = 'docs';
    contents = async () => new Blob(['read here'], { type: 'text/plain' });
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('names the file, shows its text and where it stands, and Escape closes it', async () => {
    await show(file('b'), [file('a'), file('b'), file('c')]);

    await shows('text of b');
    expect(document.querySelector('h2')?.textContent).toBe('b');
    expect(document.body.textContent).toContain('2 of 3');
    await key('Escape');
    expect(closed).toBe(1);
  });

  it('moves between the files on the arrow keys and by its buttons, stopping at either end', async () => {
    await show(file('a'), [file('a'), file('b'), file('c')]);

    await key('ArrowLeft');
    await shows('1 of 3');
    expect(labelled('Previous file')?.disabled).toBe(true);
    await key('ArrowRight');
    await shows('text of b');
    await act(async () => labelled('Next file')!.click());
    await shows('text of c');
    await key('ArrowRight');
    await shows('3 of 3');
    expect(labelled('Next file')?.disabled).toBe(true);
    await key('ArrowLeft');
    await shows('text of b');
  });

  it('leaves the arrow keys to a dialog over it, which Escape closes first', async () => {
    stubServer(async () => json({ readable: false, readAt: null }));
    await show(file('a'), [file('a'), file('b')]);

    await act(async () => labelled('Details')!.click());
    await shows('Details of a');
    await key('ArrowRight');
    expect(document.body.textContent).toContain('1 of 2');
    await key('Escape');
    await vi.waitFor(() => expect(document.body.textContent).not.toContain('Details of a'));
    expect(closed).toBe(0);
    await key('ArrowRight');
    await shows('2 of 2');
  });

  it('leaves the arrow keys to a player that has the focus', async () => {
    await show(file('a'), [file('a'), file('b')]);
    const player = document.body.appendChild(document.createElement('video'));

    await key('ArrowRight', player);

    expect(document.body.textContent).toContain('1 of 2');
  });

  it('has no position or neighbours for a file that is not among those beside it', async () => {
    await show(file('a'), [file('b')]);

    await shows('text of a');
    expect(labelled('Next file')).toBeNull();
    expect(document.body.textContent).not.toContain(' of 1');
  });

  it.each<KeyboardEventInit>([{ altKey: true }, { ctrlKey: true }, { metaKey: true }, { shiftKey: true }])('leaves an arrow key pressed with %j alone', async (modifier) => {
    await show(file('a'), [file('a'), file('b')]);

    await key('ArrowRight', document, modifier);

    expect(document.body.textContent).toContain('1 of 2');
  });

  it('says more files follow the last one counted, and finds the next of them', async () => {
    moreFound = vi.fn();
    await show(file('b'), [file('a'), file('b')]);
    expect(document.body.textContent).toContain('2 of 2+');

    await key('ArrowRight');
    await act(async () => labelled('Next file')!.click());

    expect(moreFound).toHaveBeenCalledTimes(2);
  });

  it('has no Details of a file that is not live', async () => {
    detailsOn = undefined;

    await show(file('a'));

    expect(labelled('Download')).not.toBeNull();
    expect(labelled('Details')).toBeNull();
  });

  it('opens in a tab only what a tab shows', async () => {
    await show(file('a.svg', { contentType: 'image/svg+xml' }));
    expect(labelled('Open in new tab')).not.toBeNull();

    await act(async () => change(file('a.svg', { contentType: 'image/svg+xml', encrypted: true, sha256: 'other' })));

    expect(labelled('Open in new tab')).toBeNull();
  });

  it('says why a file\'s record could not be loaded', async () => {
    await act(async () => root.render(
      <FilePreview file={undefined} error={new Error('The server is unreachable.')} reading={{ source, contents }} neighbours={null} onMoveTo={() => {}} onClose={() => {}} />,
    ));

    expect(document.body.textContent).toContain('The server is unreachable.');
    expect(labelled('Download')).toBeNull();
  });

  it('shows a file anew once its contents change, and as it is when only its name does', async () => {
    const reads = vi.spyOn(source, 'readRange');
    await show(file('a'));
    await shows('text of a');

    await act(async () => change(file('a', { name: 'renamed' })));
    await shows('renamed');
    expect(reads).toHaveBeenCalledTimes(1);
    await act(async () => change(file('a', { sha256: 'changed' })));

    await vi.waitFor(() => expect(reads).toHaveBeenCalledTimes(2));
  });

  it('says a viewer failed, and saves the file, when it throws while rendering; the next file shows', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const broken = file('broken', { contentType: 'x-test/broken' });
    await show(broken, [broken, file('b')]);

    await shows('The viewer failed while showing this file.');
    await press('Download');
    expect(saved).toEqual(['/open/broken?download']);
    await key('ArrowRight');
    await shows('text of b');
  });

  it('says there is no preview of a type no viewer takes, and saves the file', async () => {
    await show(file('a.zip', { contentType: 'application/zip' }));

    await shows('There is no preview of files of this type.');
    await press('Download');
    expect(saved).toEqual(['/open/a.zip?download']);
  });

  it('shows HTML as its source', async () => {
    await show(file('<b>page</b>', { contentType: 'text/html' }));

    await shows('text of <b>page</b>');
  });

  describe('of a blob-backed file', () => {
    beforeEach(() => vi.stubGlobal('isSecureContext', false));

    it('reads its contents here, and saves those rather than reading it again', async () => {
      const reads = vi.fn(async () => new Blob(['read here'], { type: 'text/plain' }));
      contents = reads;
      await show(file('a'));
      await shows('read here');

      await act(async () => labelled('Download')!.click());

      expect(saved).toEqual(['a']);
      expect(reads).toHaveBeenCalledTimes(1);
    });

    it('reads one over 100 MiB only once asked', async () => {
      const reads = vi.fn(async () => new Blob(['read here'], { type: 'text/plain' }));
      contents = reads;
      await show(file('a', { sizeBytes: 100 * 1024 * 1024 + 1 }));
      await shows('is read into the browser to show it.');
      expect(reads).not.toHaveBeenCalled();

      await press('Load');

      await shows('read here');
    });

    it('stops reading as it moves on', async () => {
      let signal: AbortSignal | undefined;
      contents = async (each, given) => {
        if (each.id !== 'a') return new Blob(['read here'], { type: 'text/plain' });
        signal = given;
        return new Promise(() => {});
      };
      await show(file('a'), [file('a'), file('b')]);
      expect(signal?.aborted).toBe(false);

      await key('ArrowRight');

      expect(signal?.aborted).toBe(true);
      await shows('read here');
    });

    it('says why it could not be read, and reads it again when asked', async () => {
      let reachable = false;
      contents = async () => {
        if (!reachable) throw new Error('The server is unreachable.');
        return new Blob(['read here'], { type: 'text/plain' });
      };
      await show(file('a'));
      await shows('The server is unreachable.');

      reachable = true;
      await press('Try again');

      await shows('read here');
    });

    it('reads nothing of a type no viewer takes', async () => {
      const reads = vi.fn(async () => new Blob(['a']));
      contents = reads;

      await show(file('a.zip', { contentType: 'application/zip' }));

      await shows('There is no preview of files of this type.');
      expect(reads).not.toHaveBeenCalled();
    });

    it('lets go of what it read as it moves on, and as it closes', async () => {
      const revoked = vi.spyOn(URL, 'revokeObjectURL');
      const picture = (name: string) => file(name, { contentType: 'image/png' });
      contents = async () => new Blob(['a'], { type: 'image/png' });
      const image = () => document.querySelector('img')?.src;
      await show(picture('a'), [picture('a'), picture('b')]);
      const first = await vi.waitFor(() => {
        expect(image()).toMatch(/^blob:/);
        return image()!;
      });

      await key('ArrowRight');
      await vi.waitFor(() => expect(revoked.mock.calls).toEqual([[first]]));
      await vi.waitFor(() => expect(image()).toMatch(/^blob:/));
      const second = image()!;
      await act(async () => root.render(null));

      await vi.waitFor(() => expect(revoked.mock.calls).toEqual([[first], [second]]));
    });

    it('unlocks an encrypted file by where it is now', async () => {
      const paths: string[] = [];
      contents = async (each) => {
        paths.push(each.path);
        return null;
      };
      await show(file('a', { encrypted: true }));
      await shows('This file is encrypted.');

      await act(async () => change(file('a', { encrypted: true, path: '/moved/a' })));
      await press('Unlock');

      expect(paths).toEqual(['/a', '/moved/a']);
    });

    it('offers Unlock on an encrypted file no password was given for', async () => {
      let password = false;
      contents = async () => (password ? new Blob(['secret'], { type: 'text/plain' }) : null);
      await show(file('a', { encrypted: true }));
      await shows('This file is encrypted.');

      password = true;
      await press('Unlock');

      await shows('secret');
    });
  });
});
