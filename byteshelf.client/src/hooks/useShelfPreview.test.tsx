// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { liveContent } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { json, stubServer } from '../test/fakeServer';
import { shelfFile } from '../test/shelfFiles';
import { signIn } from '../test/shelfPage';
import { useShelfPreview, type ShelfPreview } from './useShelfPreview';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('./usePreviewParam', () => ({
  usePreviewParam: () => ({ id: 'b', open: () => {}, moveTo: () => {}, close: () => {}, wasOpened: true }),
  useCloseWhenGone: () => {},
}));
vi.mock('./usePluginHost', () => ({ usePluginsSettled: () => true }));
vi.mock('./useViewers', () => ({ useViewers: () => ({ viewerFor: (file: ShelfFile) => (file.contentType === 'x-test/shown' ? { viewer: {} } : null) }) }));

/** The record of the file shown, as the server answers it. */
let record: ShelfFile;

const reading = { source: liveContent('docs'), read: async () => null, unlock: async () => null };

const file = (id: string, contentType = 'x-test/shown') => shelfFile(id, { contentType });

describe('the preview over a shelf', () => {
  let root: Root;
  let preview: ShelfPreview;

  function Probe({ files }: { files: ShelfFile[] }) {
    preview = useShelfPreview('docs', { files }, reading);
    return null;
  }

  async function show(...files: ShelfFile[]): Promise<{ previous?: string; next?: string }> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<QueryClientProvider client={client}><Probe files={files} /></QueryClientProvider>));
    await vi.waitFor(() => expect(preview.shown.previewed?.file.description).toBe('answered'));
    return { previous: preview.shown.previous?.file.id, next: preview.shown.next?.file.id };
  }

  beforeEach(() => {
    signIn();
    record = { ...file('b'), description: 'answered' };
    stubServer(async () => json(record));
    root = createRoot(document.createElement('div'));
  });

  afterEach(() => {
    act(() => root.unmount());
    vi.unstubAllGlobals();
  });

  it('has the files before and after the one shown that a viewer takes', async () => {
    expect(await show(file('a'), file('x', 'x-test/other'), file('b'), file('y', 'x-test/other'), file('c'))).toEqual({ previous: 'a', next: 'c' });
    expect(preview.shown.next?.reading).toBe(reading);
  });

  it('has none where there is none, nor beside a file that is not listed', async () => {
    expect(await show(file('b'))).toEqual({ previous: undefined, next: undefined });
    expect(await show(file('a'), file('c'))).toEqual({ previous: undefined, next: undefined });
  });

  it('goes on with what was made from the file as its own record loads', async () => {
    record = { ...record, derived: { thumbnail: 'images-1', preview: 'images-1' } };

    await show({ ...file('b'), derived: { thumbnail: 'images-1' } });

    expect(preview.shown.previewed?.file.derived).toEqual({ thumbnail: 'images-1', preview: 'images-1' });
  });
});
