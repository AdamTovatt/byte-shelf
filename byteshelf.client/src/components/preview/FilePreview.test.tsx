// @vitest-environment happy-dom
import { act, lazy, useEffect, useState, type ComponentType } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FilePreview } from './FilePreview';
import { CLEAR_OF_BARS } from './previewBars';
import { BUILT_IN_VIEWERS } from './viewers';
import { usePluginId } from '../../contexts/PluginIdentity';
import { neighboursOf } from '../../lib/preview/neighbours';
import type { FileActions } from '../../hooks/useFileActions';
import type { OfferedAction } from '../../lib/plugins/offeredActions';
import type { ViewerProps } from '../../lib/preview/viewerRegistry';
import type { ContentSource } from '../../services/contentService';
import type { FileReading } from '../../services/fileReading';
import type { ShelfFile } from '../../services/shelfService';
import { removePdfViewer, restorePdfViewer, stubTabs } from '../../test/browser';
import { button, dialogTitles, labelled, press, shows, untilFound } from '../../test/dom';
import { DRAGS_ITSELF } from '../../lib/dragsItself';
import { doubleTap, drag, layOutZoom, pointer } from '../../test/pointers';
import { useZoom } from '../../hooks/useZoom';
import { json, stubServer } from '../../test/fakeServer';
import { shelfFile } from '../../test/shelfFiles';

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

/** Shows whose it is, as one that reads the plugin's documents must know. */
function OwnedViewer() {
  return <p>of the plugin {usePluginId()}</p>;
}

/** Shows what plugins made from its file and from those beside it, whose previews it loads ahead, as one stepped to next. */
function DerivedViewer({ file, derived, neighbours }: ViewerProps) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => void derived.url('preview').then(setUrl), [derived]);
  useEffect(() => neighbours.forEach((each) => void each.derived.url('preview')), [neighbours]);
  return <p>{`${file.id} at ${url}, beside ${neighbours.map((each) => `${each.file.id} ${each.derived.has('preview')}`).join(' and ')}`}</p>;
}

/** Shows a file zoomed in, as `useZoom` marks its frame then. */
function ZoomedViewer() {
  return <div {...{ [DRAGS_ITSELF]: true }}><p>zoomed in</p></div>;
}

/** Shows a picture that zooms, as the image viewers do. */
function ZoomingViewer() {
  const { frame, zoomed } = useZoom<HTMLDivElement>();
  return <div {...frame} data-testid="frame"><div {...zoomed}>a picture</div></div>;
}

/** Shows what moves with a drag of its own: a player, a slider, a row that scrolls sideways and a pane that scrolls down, which the test page does not lay out. */
function DraggingViewer() {
  return (
    <div>
      <video data-testid="player" />
      <input type="range" />
      <div style={{ overflowX: 'auto' }} data-testid="row"><p>a long row</p></div>
      <div style={{ overflowY: 'auto' }} data-testid="pane"><p>a long pane</p></div>
      <p>plain</p>
    </div>
  );
}

/** Leaves every file to the viewer after it, as one does that has nothing to show of a file. */
function DecliningViewer({ Fallback }: ViewerProps) {
  return <Fallback />;
}

/** Brings the component of a viewer that loads it when it is first shown. */
let loadLazy: (module: { default: ComponentType<ViewerProps> }) => void;

const LazyViewer = lazy(() => new Promise<{ default: ComponentType<ViewerProps> }>((resolve) => { loadLazy = resolve; }));

const WITH_PLUGINS = BUILT_IN_VIEWERS.with('test', [
  { id: 'broken', mediaTypes: ['x-test/broken', 'x-test/broken-next'], extensions: [], component: BrokenViewer },
  { id: 'lazy', mediaTypes: ['x-test/lazy'], extensions: [], component: LazyViewer },
  { id: 'owned', mediaTypes: ['x-test/owned'], extensions: [], component: OwnedViewer },
  { id: 'derived', mediaTypes: ['x-test/derived'], extensions: [], component: DerivedViewer },
  { id: 'zoomed', mediaTypes: ['x-test/zoomed'], extensions: [], component: ZoomedViewer },
  { id: 'dragging', mediaTypes: ['x-test/dragging'], extensions: [], component: DraggingViewer },
  { id: 'zooming', mediaTypes: ['x-test/zooming'], extensions: [], component: ZoomingViewer },
  { id: 'declining', mediaTypes: ['x-test/chain', 'x-test/alone', 'x-test/gone', 'text/plain'], extensions: [], component: DecliningViewer },
]).with('zz', [
  { id: 'next', mediaTypes: ['x-test/chain'], extensions: [], component: OwnedViewer },
  { id: 'unavailable', mediaTypes: ['x-test/gone'], extensions: [], component: OwnedViewer, unavailable: () => 'This browser has no such viewer.' },
]).with('aa', [{ id: 'declining', mediaTypes: ['x-test/broken-next'], extensions: [], component: DecliningViewer }])
  .with(null, [{ id: 'own', mediaTypes: ['x-test/own'], extensions: [], component: OwnedViewer }]);

vi.mock('../../hooks/useViewers', () => ({ useViewers: () => WITH_PLUGINS }));

/** Whether plugins still loading are waited for. */
const plugins = vi.hoisted(() => ({ awaited: false }));

vi.mock('../../hooks/usePluginHost', () => ({ usePluginsAwaited: () => plugins.awaited }));

const CONVERT: OfferedAction = { pluginId: 'a', key: 'a convert', action: { id: 'convert', label: 'Convert', showsFor: () => true, run: () => undefined } };

const LIMIT = 100 * 1024 * 1024;

const source: ContentSource = {
  navigationUrl: async (id, download) => `/open/${id}${download ? '?download' : ''}`,
  read: async () => new Blob(['whole']),
  readRange: async (id) => new Blob([`text of ${id}`]),
  open: async () => new Blob(['whole']).stream(),
  derived: (id, kind) => ({ navigationUrl: async (version) => `/open/${id}/${kind}?v=${version}`, version: async () => '', read: async () => new Blob([]) }),
  encryptedFolderOf: async () => null,
};

describe('the preview', () => {
  let root: Root;
  let read: FileReading['read'];
  let unlock: FileReading['unlock'];
  let asksPassword: boolean;
  let closed: number;

  /** Shows another record of the file previewed, as the page does once the shelf has changed. */
  let change: (file: ShelfFile) => void;
  let moreFound: (() => void) | undefined;
  let detailsOn: string | undefined;
  let actions: FileActions | undefined;

  function Page({ files, first }: { files: ShelfFile[]; first: ShelfFile }) {
    const [shown, setShown] = useState(first);
    useEffect(() => {
      change = setShown;
    }, []);
    const beside = neighboursOf(files, shown.id);
    const move = (to: ShelfFile | null) => to && (() => setShown(to));
    const reading: FileReading = { source, read: (each, signal) => read(each, signal), unlock: (each, signal) => unlock(each, signal) };
    const readable = (file: ShelfFile | null | undefined) => file ? { file, reading } : null;
    return (
      <FilePreview
        previewed={{ file: shown, reading }}
        error={null}
        steps={beside && { ...beside, more: moreFound !== undefined, toPrevious: move(beside.previous), toNext: move(beside.next) ?? moreFound ?? null }}
        previous={readable(beside?.previous)}
        next={readable(beside?.next)}
        onClose={() => closed++}
        detailsOn={detailsOn}
        actions={actions}
        asksPassword={asksPassword}
      />
    );
  }

  async function show(first: ShelfFile, files: ShelfFile[] = []): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    await act(async () => root.render(<QueryClientProvider client={client}><Page files={files} first={first} /></QueryClientProvider>));
  }

  async function key(name: string, target: EventTarget = document, init: KeyboardEventInit = {}): Promise<KeyboardEvent> {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init });
    await act(async () => void target.dispatchEvent(event));
    return event;
  }

  beforeEach(() => {
    vi.stubGlobal('isSecureContext', true);
    saved.length = 0;
    closed = 0;
    asksPassword = false;
    moreFound = undefined;
    detailsOn = 'docs';
    actions = undefined;
    plugins.awaited = false;
    read = async () => new Blob(['read here'], { type: 'text/plain' });
    unlock = async () => null;
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('names the file, shows its text and where it stands, and Escape closes it', async () => {
    await show(shelfFile('b'), [shelfFile('a'), shelfFile('b'), shelfFile('c')]);

    await shows('text of b');
    expect(document.querySelector('h2')?.textContent).toBe('b');
    expect(document.body.textContent).toContain('2 of 3');
    await key('Escape');
    expect(closed).toBe(1);
  });

  it('moves between the files on the arrow keys and by its buttons, stopping at either end', async () => {
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b'), shelfFile('c')]);

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

  describe('its controls', () => {
    const files = [shelfFile('a'), shelfFile('b'), shelfFile('c')];

    beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));

    afterEach(() => {
      vi.useRealTimers();
      restorePdfViewer();
    });

    /** Whether the bars of controls show. */
    function controlsShown(): boolean {
      return document.querySelector('[data-shown]')!.getAttribute('data-shown') === 'true';
    }

    function idle(ms: number): void {
      act(() => vi.advanceTimersByTime(ms));
    }

    /** A tap of a finger on `target`, which the browser follows with a click. */
    function tap(target: HTMLElement): void {
      pointer('pointerdown', target, { pointerType: 'touch' });
      act(() => target.click());
    }

    /** One finger put down at `from` and lifted at `to`, as the test page has no touch events of its own. */
    function swipe(target: Element, from: [number, number], to: [number, number]): void {
      const touchEvent = (type: string, touches: [number, number][], changed: [number, number]) => {
        const event = new Event(type, { bubbles: true, cancelable: true });
        const point = ([clientX, clientY]: [number, number]) => ({ clientX, clientY });
        return Object.assign(event, { touches: touches.map(point), changedTouches: [point(changed)] });
      };
      act(() => {
        target.dispatchEvent(touchEvent('touchstart', [from], from));
        target.dispatchEvent(touchEvent('touchend', [], to));
      });
    }

    /**
     * Lays out the picture `ZoomingViewer` shows, 400 by 200 in a frame 400 by 300, and zooms it in with a double tap:
     * 300 pixels from either side and 100 from the top or the bottom.
     */
    function zoomIn(): HTMLElement {
      const frame = document.querySelector<HTMLElement>('[data-testid="frame"]')!;
      layOutZoom(frame, { width: 400, height: 200 });
      doubleTap(frame, [200, 150]);
      return frame;
    }

    const text = () => document.querySelector('pre')!;

    function ancestors(element: Element): Element[] {
      return element.parentElement ? [element.parentElement, ...ancestors(element.parentElement)] : [];
    }

    it('go after two seconds of nothing, and come back as the mouse moves or at a click, not as a key moves on', async () => {
      await show(files[0], files);
      await shows('text of a');

      expect(controlsShown()).toBe(true);
      idle(2000);
      expect(controlsShown()).toBe(false);
      await key('ArrowRight');
      await shows('text of b');
      expect(controlsShown()).toBe(false);

      pointer('pointermove', text(), { pointerType: 'mouse', clientX: 10, clientY: 10 });
      expect(controlsShown()).toBe(true);
      idle(1000);
      // A move told of where the mouse already was is what is under it changing, and brings nothing back.
      pointer('pointermove', text(), { pointerType: 'mouse', clientX: 10, clientY: 10 });
      idle(1000);
      expect(controlsShown()).toBe(false);

      act(() => text().click());
      expect(controlsShown()).toBe(true);
    });

    it('stay while the mouse is on them, and go two seconds after it leaves', async () => {
      await show(files[0], files);
      await shows('text of a');
      // Over one of them, and out from it onto the file.
      pointer('pointerover', labelled('Close')!, { pointerType: 'mouse' });
      idle(5000);
      expect(controlsShown()).toBe(true);
      pointer('pointerout', labelled('Close')!, { pointerType: 'mouse', relatedTarget: text() });
      idle(1999);
      expect(controlsShown()).toBe(true);
      idle(1);
      expect(controlsShown()).toBe(false);
    });

    it('are hidden and shown again by a tap, and kept by one on a control', async () => {
      await show(files[0], files);
      await shows('text of a');

      tap(text());
      expect(controlsShown()).toBe(false);
      tap(text());
      expect(controlsShown()).toBe(true);
      tap(labelled('Download')!);
      expect(controlsShown()).toBe(true);
    });

    it('move on at a swipe, left to the next file and right to the previous, which shows no controls', async () => {
      await show(files[1], files);
      await shows('text of b');
      idle(2000);

      swipe(text(), [300, 200], [100, 220]);
      await shows('text of c');
      swipe(text(), [100, 200], [300, 180]);
      await shows('text of b');
      expect(controlsShown()).toBe(false);
    });

    it('take no swipe that is short, or more up or down than across', async () => {
      await show(files[1], files);
      await shows('text of b');

      swipe(text(), [300, 200], [260, 200]);
      swipe(text(), [300, 100], [200, 300]);
      await act(async () => {});
      expect(document.querySelector('h2')?.textContent).toBe('b');
    });

    it('close at a swipe down, as Close does, and not at one short, slanted or up', async () => {
      await show(files[1], files);
      await shows('text of b');

      swipe(text(), [200, 100], [200, 199]);
      swipe(text(), [200, 100], [130, 200]);
      swipe(text(), [200, 300], [200, 100]);
      expect(closed).toBe(0);
      swipe(text(), [200, 100], [230, 200]);
      expect(closed).toBe(1);
    });

    it('take no swipe down that starts on a pane scrolled from its top, a player or a slider', async () => {
      const dragging = [shelfFile('y', { contentType: 'x-test/dragging' })];
      await show(dragging[0], dragging);
      await shows('a long pane');
      const pane = document.querySelector<HTMLElement>('[data-testid="pane"]')!;
      Object.defineProperty(pane, 'scrollTop', { value: 40, configurable: true });

      for (const start of [pane.querySelector('p')!, document.querySelector('video')!, document.querySelector('input')!]) swipe(start, [200, 100], [200, 300]);
      expect(closed).toBe(0);

      Object.defineProperty(pane, 'scrollTop', { value: 0 });
      swipe(pane.querySelector('p')!, [200, 100], [200, 300]);
      expect(closed).toBe(1);
    });

    it('take a swipe down that starts on a row that scrolls sideways, and one across on a pane scrolled from its top', async () => {
      const dragging = [shelfFile('y', { contentType: 'x-test/dragging' }), shelfFile('z', { contentType: 'x-test/dragging' })];
      await show(dragging[0], dragging);
      await shows('a long row');
      const row = document.querySelector<HTMLElement>('[data-testid="row"]')!;
      Object.defineProperties(row, { scrollWidth: { value: 800 }, clientWidth: { value: 400 } });
      Object.defineProperty(document.querySelector('[data-testid="pane"]'), 'scrollTop', { value: 40 });

      swipe(row.querySelector('p')!, [200, 100], [200, 300]);
      expect(closed).toBe(1);
      swipe(document.querySelector('[data-testid="pane"] p')!, [300, 200], [100, 200]);
      await shows('2 of 2');
    });

    it('move on at a drag of a finger past the edge of what is zoomed in, to the next shown fitted, and close at one down past it', async () => {
      const zooming = [shelfFile('y', { contentType: 'x-test/zooming' }), shelfFile('z', { contentType: 'x-test/zooming' })];
      await show(zooming[0], zooming);
      await shows('a picture');

      drag(zoomIn(), [[350, 150], [0, 150]]);
      await shows('2 of 2');
      expect(document.querySelector(`[${DRAGS_ITSELF}]`)).toBeNull();

      drag(zoomIn(), [[200, 50], [200, 250]]);
      expect(closed).toBe(1);
    });

    it('leave a swipe to what is zoomed in, which a drag moves', async () => {
      const zoomed = [shelfFile('y', { contentType: 'x-test/zoomed' }), shelfFile('z', { contentType: 'x-test/zoomed' })];
      await show(zoomed[0], zoomed);
      await shows('zoomed in');

      swipe(document.querySelector(`[${DRAGS_ITSELF}] p`)!, [300, 200], [100, 200]);
      await act(async () => {});
      expect(document.querySelector('h2')?.textContent).toBe('y');
    });

    it('stay while the mouse is over a PDF, whose moves the page does not hear, which they keep clear of', async () => {
      await show(shelfFile('a.pdf', { contentType: 'application/pdf' }));
      const frame = await untilFound<HTMLIFrameElement>('iframe');

      pointer('pointerover', frame, { pointerType: 'mouse' });
      idle(5000);
      expect(controlsShown()).toBe(true);
      // Back on the page.
      pointer('pointermove', document.querySelector('h2')!, { pointerType: 'mouse', clientX: 5, clientY: 5 });
      idle(2000);
      expect(controlsShown()).toBe(false);

      expect(ancestors(frame).some((each) => each.className.includes(CLEAR_OF_BARS))).toBe(true);
      expect(frame.closest<HTMLElement>('[style*="--preview-bar-top"]')).not.toBeNull();
    });

    it('go under no viewer that is not clear of them', async () => {
      await show(files[0], files);
      await shows('text of a');

      expect(ancestors(text()).some((each) => each.className.includes(CLEAR_OF_BARS))).toBe(false);
    });

    it('are kept by a tap on a button of what is shown, and by a dialog over the preview', async () => {
      removePdfViewer();
      await show(shelfFile('a.pdf', { contentType: 'application/pdf' }), [shelfFile('a.pdf', { contentType: 'application/pdf' })]);
      await shows('This browser does not show a PDF inside a page.');

      tap(button('Download')!);
      expect(controlsShown()).toBe(true);

      await act(async () => labelled('Details')!.click());
      const backdrops = document.querySelectorAll<HTMLElement>('[data-testid="modal-backdrop"]');
      tap(backdrops[backdrops.length - 1]);
      expect(dialogTitles()).toEqual(['a.pdf']);
      expect(controlsShown()).toBe(true);
    });

    it('take no swipe that starts on a player, a slider or a row that scrolls sideways', async () => {
      const dragging = [shelfFile('y', { contentType: 'x-test/dragging' }), shelfFile('z', { contentType: 'x-test/dragging' })];
      await show(dragging[0], dragging);
      await shows('a long row');
      const row = document.querySelector<HTMLElement>('[data-testid="row"]')!;
      Object.defineProperty(row, 'scrollWidth', { value: 800 });
      Object.defineProperty(row, 'clientWidth', { value: 400 });

      for (const start of [document.querySelector('video')!, document.querySelector('input')!, row.querySelector('p')!]) swipe(start, [300, 200], [100, 200]);
      await act(async () => {});
      expect(document.querySelector('h2')?.textContent).toBe('y');

      swipe(row.parentElement!.lastElementChild!, [300, 200], [100, 200]);
      await shows('2 of 2');
    });

    it('stay while the focus from the keyboard is on them, and not for a button clicked', async () => {
      await show(files[0], files);
      await shows('text of a');
      const close = labelled('Close')!;

      // The test page does not tell focus from the keyboard from a click's.
      vi.spyOn(close, 'matches').mockImplementation((selector) => selector === ':focus-visible');
      act(() => close.focus());
      idle(5000);
      expect(controlsShown()).toBe(true);
      act(() => close.blur());
      idle(2000);
      expect(controlsShown()).toBe(false);

      vi.spyOn(close, 'matches').mockReturnValue(false);
      act(() => close.focus());
      act(() => close.click());
      expect(controlsShown()).toBe(true);
      idle(2000);
      expect(controlsShown()).toBe(false);
    });
  });

  describe('the page under it', () => {
    const page = document.documentElement.style;

    afterEach(() => {
      page.overscrollBehaviorX = '';
      page.overscrollBehaviorY = '';
    });

    it('is not pulled down to reload while it is open, and is left as it was once it closes', async () => {
      page.overscrollBehaviorY = 'contain';
      await show(shelfFile('a'));

      expect([page.overscrollBehaviorX, page.overscrollBehaviorY]).toEqual(['', 'none']);
      act(() => root.unmount());
      expect(page.overscrollBehaviorY).toBe('contain');
    });
  });

  describe('its bars', () => {
    const files = [shelfFile('a'), shelfFile('b'), shelfFile('c')];

    /** Makes the screen `width` wide, which the bars ask of the browser; 1024 by 768 is the test page's own. */
    function widen(width: number): void {
      act(() => (window as unknown as { happyDOM: { setViewport(size: { width: number; height: number }): void } }).happyDOM.setViewport({ width, height: 768 }));
    }

    afterEach(() => widen(1024));

    /** The sizes of the glyphs of the buttons in the bars, which the test page draws as elements named by their files. */
    function glyphSizes(): (string | null | undefined)[] {
      return [...new Set([...document.querySelectorAll('[data-shown] button')].map((each) => each.firstElementChild?.getAttribute('width')))];
    }

    it('hold larger buttons on a phone than from Tailwind\'s sm up', async () => {
      actions = { actionsFor: () => [CONVERT], run: vi.fn() };
      widen(375);
      await show(files[1], files);
      await shows('text of b');

      expect(labelled('More actions')).not.toBeNull();
      expect(glyphSizes()).toEqual(['20']);
      widen(640);
      expect(glyphSizes()).toEqual(['16']);
    });
  });

  it('closes at a click beside a picture, as at Close', async () => {
    await show(shelfFile('a', { contentType: 'x-test/zooming' }));
    await shows('a picture');
    const frame = document.querySelector('[data-testid="frame"]')!;
    // The test page lays nothing out: the picture takes no room, and the click is beside it.
    frame.setPointerCapture = () => {};

    act(() => {
      frame.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerId: 1, clientX: 200, clientY: 20, button: 0, pointerType: 'mouse' }));
      frame.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 200, clientY: 20 }));
    });
    expect(closed).toBe(1);
  });

  it('gives a viewer what plugins made from the file and from those beside it', async () => {
    const derived = (id: string, kinds: ShelfFile['derived']) => shelfFile(id, { contentType: 'x-test/derived', derived: kinds });
    const files = [derived('a', null), derived('b', { preview: 'p-1' }), derived('c', { preview: 'p-1' })];
    await show(files[1], files);

    await shows('b at /open/b/preview?v=b-p-1, beside a false and c true');
    await key('ArrowRight');
    await shows('c at /open/c/preview?v=c-p-1, beside b true');

    // Each file's is its own.
    await act(async () => change(derived('a', { preview: 'p-2' })));
    await shows('a at /open/a/preview?v=a-p-2, beside b true');
  });

  it('leaves the arrow keys to a dialog over it, which Escape closes first', async () => {
    stubServer(async () => json({ readable: false, readAt: null }));
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);

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

  it.each([
    ['a player', 'video'],
    ['a field', 'input'],
  ])('leaves the arrow keys to %s that has the focus', async (_what, tag) => {
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);
    const focused = document.body.appendChild(document.createElement(tag));

    await key('ArrowRight', focused);

    expect(document.body.textContent).toContain('1 of 2');
  });

  it('leaves an arrow key that a viewer took alone', async () => {
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);
    const taking = document.body.appendChild(document.createElement('div'));
    taking.addEventListener('keydown', (event) => event.preventDefault());

    await key('ArrowRight', taking);

    expect(document.body.textContent).toContain('1 of 2');
  });

  it('keeps an arrow key that moves on from scrolling what shows, and no other', async () => {
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);

    expect((await key('ArrowLeft')).defaultPrevented).toBe(false);
    expect((await key('ArrowRight')).defaultPrevented).toBe(true);
  });

  it('has no position or neighbours for a file that is not among those beside it', async () => {
    await show(shelfFile('a'), [shelfFile('b')]);

    await shows('text of a');
    expect(labelled('Next file')).toBeNull();
    expect(document.body.textContent).not.toContain(' of 1');
  });

  it.each<KeyboardEventInit>([{ altKey: true }, { ctrlKey: true }, { metaKey: true }, { shiftKey: true }])('leaves an arrow key pressed with %j alone', async (modifier) => {
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);

    await key('ArrowRight', document, modifier);

    expect(document.body.textContent).toContain('1 of 2');
  });

  it('says more files follow the last one counted, and finds the next of them', async () => {
    moreFound = vi.fn();
    await show(shelfFile('b'), [shelfFile('a'), shelfFile('b')]);
    expect(document.body.textContent).toContain('2 of 2+');

    await key('ArrowRight');
    await act(async () => labelled('Next file')!.click());

    expect(moreFound).toHaveBeenCalledTimes(2);
  });

  it('has no Details of a file that is not live', async () => {
    detailsOn = undefined;

    await show(shelfFile('a'));

    expect(labelled('Download')).not.toBeNull();
    expect(labelled('Details')).toBeNull();
  });

  it('opens in a tab only what a tab shows', async () => {
    await show(shelfFile('a.svg', { contentType: 'image/svg+xml' }));
    expect(labelled('Open in new tab')).not.toBeNull();

    await act(async () => change(shelfFile('a.svg', { contentType: 'image/svg+xml', encrypted: true, sha256: 'other' })));

    expect(labelled('Open in new tab')).toBeNull();
  });

  it('says why a file\'s record could not be loaded', async () => {
    await act(async () => root.render(
      <FilePreview previewed={null} error={new Error('The server is unreachable.')} steps={null} previous={null} next={null} onClose={() => {}} asksPassword={false} />,
    ));

    expect(document.body.textContent).toContain('The server is unreachable.');
    expect(labelled('Download')).toBeNull();
  });

  it('shows a file anew once its contents change, and as it is when only its name does', async () => {
    const reads = vi.spyOn(source, 'readRange');
    await show(shelfFile('a'));
    await shows('text of a');

    await act(async () => change(shelfFile('a', { name: 'renamed' })));
    await shows('renamed');
    expect(reads).toHaveBeenCalledTimes(1);
    await act(async () => change(shelfFile('a', { sha256: 'changed' })));

    await vi.waitFor(() => expect(reads).toHaveBeenCalledTimes(2));
  });

  it('says a viewer failed, and saves the file, when it throws while rendering; the next file shows', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const broken = shelfFile('broken', { contentType: 'x-test/broken' });
    await show(broken, [broken, shelfFile('b')]);

    await shows('The viewer failed while showing this file.');
    await press('Download');
    expect(saved).toEqual(['/open/broken?download']);
    await key('ArrowRight');
    await shows('text of b');
  });

  it('loads a viewer\'s component as it is first shown', async () => {
    await show(shelfFile('a', { contentType: 'x-test/lazy' }));
    expect(document.querySelector('[role="status"]')).not.toBeNull();

    await act(async () => loadLazy({ default: ({ file: { name } }) => <p>drawn late: {name}</p> }));

    await shows('drawn late: a');
  });

  it('waits for plugins still loading before it shows a file', async () => {
    plugins.awaited = true;
    await show(shelfFile('a'));
    expect(document.body.textContent).not.toContain('text of a');

    plugins.awaited = false;
    await act(async () => change(shelfFile('a')));
    await shows('text of a');
  });

  it('waits for plugins still loading before it says that no viewer takes a file', async () => {
    const zip = shelfFile('b.zip', { contentType: 'application/zip' });
    plugins.awaited = true;
    await show(zip);
    expect(document.body.textContent).not.toContain('There is no preview');

    plugins.awaited = false;
    await act(async () => change({ ...zip }));
    await shows('There is no preview of files of this type.');
  });

  it('shows a file with a plugin\'s viewer as that plugin\'s, and with one of the app\'s own as no plugin\'s', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await show(shelfFile('a', { contentType: 'x-test/owned' }));
    await shows('of the plugin test');

    await act(async () => change(shelfFile('b', { contentType: 'x-test/own' })));
    await shows('The viewer failed while showing this file.');
  });

  it('shows a file a viewer leaves to the next in that one, as its plugin\'s, and the app\'s own after a plugin\'s', async () => {
    await show(shelfFile('a', { contentType: 'x-test/chain' }));
    await shows('of the plugin zz');

    await act(async () => change(shelfFile('b.txt', { contentType: 'text/plain' })));
    await shows('text of b.txt');
  });

  it('says no other viewer shows a file that the last viewer leaves, and saves it', async () => {
    await show(shelfFile('a.bin', { contentType: 'x-test/alone' }));

    await shows('No other viewer shows this file.');
    await press('Download');
    expect(saved).toEqual(['/open/a.bin?download']);
  });

  it('says why the viewer after one that leaves a file cannot show it in this browser', async () => {
    await show(shelfFile('a.bin', { contentType: 'x-test/gone' }));

    await shows('This browser has no such viewer.');
  });

  it('says a viewer failed, and saves the file, when the one a file is left to throws', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    await show(shelfFile('a.bin', { contentType: 'x-test/broken-next' }));

    await shows('The viewer failed while showing this file.');
    await press('Download');
    expect(saved).toEqual(['/open/a.bin?download']);
  });

  it('has the plugins\' actions for a live file in a menu of their own, and none without any', async () => {
    const run = vi.fn(async () => undefined);
    actions = { actionsFor: ({ name }) => (name === 'a' ? [CONVERT] : []), run };
    await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);

    await act(async () => labelled('More actions')!.click());
    await press('Convert');
    expect(run.mock.calls).toEqual([[CONVERT, expect.objectContaining({ file: expect.objectContaining({ name: 'a' }), source })]]);

    await key('ArrowRight');
    await shows('text of b');
    expect(labelled('More actions')).toBeNull();

    actions = undefined;
    await act(async () => change(shelfFile('a')));
    await shows('text of a');
    expect(labelled('More actions')).toBeNull();
  });

  it('gives an action the contents it is reading itself, which the action may give up', async () => {
    const run = vi.fn<FileActions['run']>(async () => undefined);
    actions = { actionsFor: () => [CONVERT], run };
    let bring: (contents: Blob) => void = () => undefined;
    const reads = vi.fn(() => new Promise<Blob>((resolve) => { bring = resolve; }));
    read = reads;
    await show(shelfFile('a', { encrypted: true }));
    await act(async () => labelled('More actions')!.click());
    await press('Convert');
    const [, target] = run.mock.calls[0];
    const giving = new AbortController();
    const given = target.readWhole(giving.signal);

    giving.abort();
    await expect(given).rejects.toMatchObject({ name: 'AbortError' });

    await act(async () => bring(new Blob(['read here'])));
    await shows('read here');
    expect(reads).toHaveBeenCalledOnce();
  });

  it('says there is no preview of a type no viewer takes, and saves the file', async () => {
    await show(shelfFile('a.zip', { contentType: 'application/zip' }));

    await shows('There is no preview of files of this type.');
    await press('Download');
    expect(saved).toEqual(['/open/a.zip?download']);
  });

  it('shows HTML as its source', async () => {
    await show(shelfFile('<b>page</b>', { contentType: 'text/html' }));

    await shows('text of <b>page</b>');
  });

  describe('of a PDF', () => {
    const pdf = shelfFile('a.pdf', { contentType: 'application/pdf' });

    afterEach(restorePdfViewer);

    it('shows it in a frame, by its address', async () => {
      await show(pdf);

      const frame = await untilFound<HTMLIFrameElement>('iframe');
      expect([frame.getAttribute('src'), frame.title]).toEqual(['/open/a.pdf', 'a.pdf']);
    });

    it('says the browser has no viewer of its own for one, offering a tab and Download', async () => {
      removePdfViewer();
      const tabs = stubTabs();

      await show(pdf);

      await shows('This browser does not show a PDF inside a page.');
      expect(document.querySelector('iframe')).toBeNull();
      await press('Open in new tab');
      await vi.waitFor(() => expect(tabs.map((tab) => tab.location.href)).toEqual(['/open/a.pdf']));
      await press('Download');
      expect(saved).toEqual(['/open/a.pdf?download']);
    });

    it('shows an encrypted one in a frame, from its contents read here', async () => {
      read = async () => new Blob(['%PDF'], { type: 'application/pdf' });

      await show({ ...pdf, encrypted: true });

      await vi.waitFor(() => expect(document.querySelector('iframe')?.src).toMatch(/^blob:/));
    });

    it('reads nothing of an encrypted one the browser does not show', async () => {
      removePdfViewer();
      const reads = vi.fn(read);
      read = reads;

      await show({ ...pdf, encrypted: true });

      await shows('This browser does not show a PDF inside a page.');
      expect(reads).not.toHaveBeenCalled();
    });

    it('says why it has no address, and shows it when Try again is pressed once it has', async () => {
      vi.spyOn(source, 'navigationUrl').mockRejectedValueOnce(new Error('The session has ended.'));

      await show(pdf);
      await shows('The session has ended.');

      vi.mocked(source.navigationUrl).mockResolvedValue('/open/again');
      await press('Try again');
      expect((await untilFound('iframe')).getAttribute('src')).toBe('/open/again');
    });
  });

  describe('of a blob-backed file', () => {
    beforeEach(() => vi.stubGlobal('isSecureContext', false));

    it('reads its contents here, and saves those rather than reading it again', async () => {
      const reads = vi.fn(async () => new Blob(['read here'], { type: 'text/plain' }));
      read = reads;
      await show(shelfFile('a'));
      await shows('read here');

      await act(async () => labelled('Download')!.click());

      expect(saved).toEqual(['a']);
      expect(reads).toHaveBeenCalledTimes(1);
    });

    it('reads one over 100 MiB only once asked', async () => {
      const reads = vi.fn(async () => new Blob(['read here'], { type: 'text/plain' }));
      read = reads;
      const large = (name: string) => shelfFile(name, { sizeBytes: LIMIT + 1 });
      await show(large('a'), [large('a'), large('b')]);
      await shows('is read into the browser to show it.');
      expect(reads).not.toHaveBeenCalled();

      await press('Load');

      await shows('read here');
      await key('ArrowRight');
      await shows('is read into the browser to show it.');
      expect(reads).toHaveBeenCalledTimes(1);
    });

    it('stops reading as it moves on', async () => {
      let signal: AbortSignal | undefined;
      read = async (each, aborts) => {
        if (each.id !== 'a') return new Blob(['read here'], { type: 'text/plain' });
        signal = aborts;
        return new Promise(() => {});
      };
      await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);
      expect(signal?.aborted).toBe(false);

      await key('ArrowRight');

      expect(signal?.aborted).toBe(true);
      await shows('read here');
    });

    it('says why it could not be read, and reads it again when asked', async () => {
      let reachable = false;
      read = async () => {
        if (!reachable) throw new Error('The server is unreachable.');
        return new Blob(['read here'], { type: 'text/plain' });
      };
      await show(shelfFile('a'));
      await shows('The server is unreachable.');

      reachable = true;
      await press('Try again');

      await shows('read here');
    });

    it('reads what plugins made of a file beside it once, as it moves on to it', async () => {
      const reads: string[] = [];
      const derivedOf = source.derived;
      vi.spyOn(source, 'derived').mockImplementation((id, kind) => ({
        ...derivedOf(id, kind),
        read: async () => {
          reads.push(`${id} ${kind}`);
          return new Blob(['made'], { type: 'image/jpeg' });
        },
      }));
      const made = (id: string) => shelfFile(id, { contentType: 'x-test/derived', derived: { preview: 'p-1' } });
      const files = [made('a'), made('b'), made('c')];
      await show(files[0], files);
      await shows('a at blob:');
      await vi.waitFor(() => expect([...reads].sort()).toEqual(['a preview', 'b preview']));

      await key('ArrowRight');

      await shows('b at blob:');
      await vi.waitFor(() => expect([...reads].sort()).toEqual(['a preview', 'b preview', 'c preview']));
    });

    it('reads nothing of a type no viewer takes', async () => {
      const reads = vi.fn(async () => new Blob(['a']));
      read = reads;

      await show(shelfFile('a.zip', { contentType: 'application/zip' }));

      await shows('There is no preview of files of this type.');
      expect(reads).not.toHaveBeenCalled();
    });

    it('lets go of what it read as it moves on, and as it closes', async () => {
      const revoked = vi.spyOn(URL, 'revokeObjectURL');
      const picture = (name: string) => shelfFile(name, { contentType: 'image/png' });
      read = async () => new Blob(['a'], { type: 'image/png' });
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

    describe('and locked', () => {
      const locked = shelfFile('a', { encrypted: true });
      let asks: { path: string; signal?: AbortSignal; answer: (given: boolean) => void }[];

      beforeEach(() => {
        asks = [];
        read = async (each) => (each.encrypted ? null : new Blob([`secret of ${each.id}`], { type: 'text/plain' }));
        unlock = (each, signal) => new Promise((resolve) => {
          asks.push({
            path: each.path,
            signal,
            answer: (given) => resolve(given ? new Blob([`secret of ${each.id}`]).stream() : null),
          });
        });
      });

      it('is unlocked once asked, by where it is now', async () => {
        await show(locked);
        await shows('This file is encrypted.');
        expect(asks).toEqual([]);

        await act(async () => change({ ...locked, path: '/moved/a' }));
        await press('Unlock');
        await act(async () => asks[0].answer(true));

        await shows('secret of a');
        expect(asks.map(({ path }) => path)).toEqual(['/moved/a']);
      });

      it('has its password asked for once as it shows when told to, and stays locked when that is cancelled', async () => {
        asksPassword = true;
        await show(locked);
        await shows('This file is encrypted.');
        expect(asks).toHaveLength(1);

        await act(async () => asks[0].answer(false));
        expect(asks).toHaveLength(1);
        await press('Unlock');
        await act(async () => asks[1].answer(true));

        await shows('secret of a');
      });

      it('has its prompt taken back as it moves on', async () => {
        await show(locked, [locked, shelfFile('b')]);
        await shows('This file is encrypted.');
        await press('Unlock');
        expect(asks[0].signal?.aborted).toBe(false);

        await key('ArrowRight');

        expect(asks[0].signal?.aborted).toBe(true);
        await shows('secret of b');
      });

      it('shows once Download has unlocked it, unlocked once', async () => {
        await show(locked);
        await shows('This file is encrypted.');

        await act(async () => labelled('Download')!.click());
        await vi.waitFor(() => expect(asks).toHaveLength(1));
        await act(async () => asks[0].answer(true));

        await shows('secret of a');
        await vi.waitFor(() => expect(saved).toEqual(['a']));
        expect(asks).toHaveLength(1);
      });

      it('says why unlocking it failed, and is unlocked when asked again', async () => {
        const unlocking = unlock;
        unlock = async () => {
          throw new Error('The server is unreachable.');
        };
        await show(locked);
        await shows('This file is encrypted.');

        await press('Unlock');
        await shows('The server is unreachable.');
        unlock = unlocking;
        await press('Unlock');
        await shows('This file is encrypted.');
        await act(async () => asks[0].answer(true));

        await shows('secret of a');
      });
    });

    it('shows anew once its contents change, and as it is when only its name does', async () => {
      read = async (each) => new Blob([`read of ${each.sha256}`], { type: 'text/plain' });
      const reads = vi.fn(read);
      read = reads;
      await show(shelfFile('a'));
      await shows('read of a');

      await act(async () => change(shelfFile('a', { name: 'renamed' })));
      await shows('renamed');
      expect(reads).toHaveBeenCalledTimes(1);
      await act(async () => change(shelfFile('a', { sha256: 'changed' })));

      await shows('read of changed');
    });

    it.each([
      ['a Download while it is read', shelfFile('a'), () => {}],
      ['a Download that starts reading it', shelfFile('a', { sizeBytes: LIMIT + 1 }), () => shows('is read into the browser to show it.')],
    ])('is read once for the preview and %s', async (_when, shown, untilShown) => {
      let answer: (contents: Blob) => void = () => {};
      const reads = vi.fn(() => new Promise<Blob>((resolve) => (answer = resolve)));
      read = reads;
      await show(shown);
      await untilShown();

      await act(async () => labelled('Download')!.click());
      await act(async () => answer(new Blob(['a read'], { type: 'text/plain' })));

      await shows('a read');
      await vi.waitFor(() => expect(saved).toEqual(['a']));
      expect(reads).toHaveBeenCalledTimes(1);
    });

    it('is read again by a Download once reading it failed, and then shows', async () => {
      let reachable = false;
      const reads = vi.fn(async () => {
        if (!reachable) throw new Error('The server is unreachable.');
        return new Blob(['read here'], { type: 'text/plain' });
      });
      read = reads;
      await show(shelfFile('a'));
      await shows('The server is unreachable.');

      reachable = true;
      await act(async () => labelled('Download')!.click());

      await shows('read here');
      expect(saved).toEqual(['a']);
      expect(reads).toHaveBeenCalledTimes(2);
    });

    it('goes on being saved as the preview moves on', async () => {
      let answer: (contents: Blob) => void = () => {};
      let signal: AbortSignal | undefined;
      read = (each, aborts) => {
        if (each.id !== 'a') return Promise.resolve(new Blob(['read here'], { type: 'text/plain' }));
        signal = aborts;
        return new Promise((resolve) => (answer = resolve));
      };
      await show(shelfFile('a'), [shelfFile('a'), shelfFile('b')]);
      await act(async () => labelled('Download')!.click());

      await key('ArrowRight');
      await shows('read here');
      expect(signal?.aborted).toBe(false);
      await act(async () => answer(new Blob(['a'])));

      await vi.waitFor(() => expect(saved).toEqual(['a']));
    });
  });
});
