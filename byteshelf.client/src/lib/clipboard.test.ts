import { afterEach, describe, expect, it, vi } from 'vitest';
import { copyWhenReady } from './clipboard';

/** A ClipboardItem that keeps what it was given, so a test can read the text written. */
class RecordingClipboardItem {
  readonly items: Record<string, Promise<Blob>>;

  constructor(items: Record<string, Promise<Blob>>) {
    this.items = items;
  }
}

function useClipboard(clipboard: Partial<Clipboard> | undefined, withClipboardItem = true) {
  vi.stubGlobal('navigator', { clipboard });
  vi.stubGlobal('ClipboardItem', withClipboardItem ? RecordingClipboardItem : undefined);
}

describe('copyWhenReady', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('writes the text once it arrives, through a clipboard item started at once', async () => {
    const write = vi.fn<(items: RecordingClipboardItem[]) => Promise<void>>().mockResolvedValue(undefined);
    useClipboard({ write } as unknown as Partial<Clipboard>);

    const copied = await copyWhenReady(Promise.resolve('https://files.test/api/downloads/abc'));

    expect(copied).toBe(true);
    const [item] = write.mock.calls[0][0];
    expect(await (await item.items['text/plain']).text()).toBe('https://files.test/api/downloads/abc');
  });

  it('writes plain text where the browser has no clipboard items', async () => {
    const writeText = vi.fn<(text: string) => Promise<void>>().mockResolvedValue(undefined);
    useClipboard({ writeText }, false);

    expect(await copyWhenReady(Promise.resolve('the link'))).toBe(true);
    expect(writeText).toHaveBeenCalledWith('the link');
  });

  it('answers false without a clipboard, as on plain HTTP', async () => {
    useClipboard(undefined);

    expect(await copyWhenReady(Promise.resolve('the link'))).toBe(false);
  });

  it('answers false when the browser refuses the write', async () => {
    useClipboard({ write: vi.fn().mockRejectedValue(new Error('NotAllowedError')) } as unknown as Partial<Clipboard>);

    expect(await copyWhenReady(Promise.resolve('the link'))).toBe(false);
  });
});
