import { vi } from 'vitest';

/** Stands in for the tabs this page opens, answering them as they open. Undo with vi.restoreAllMocks(). */
export function stubTabs(): { location: { href: string } }[] {
  const tabs: { location: { href: string } }[] = [];
  vi.spyOn(window, 'open').mockImplementation(() => {
    const tab = { location: { href: '' }, sessionStorage: {}, close: () => {} };
    tabs.push(tab);
    return tab as unknown as Window;
  });
  return tabs;
}

/** Makes the browser one that shows no PDF inside a page. Undo with `restorePdfViewer`. */
export function removePdfViewer(): void {
  Object.defineProperty(navigator, 'pdfViewerEnabled', { value: false, configurable: true });
}

/** The browser as happy-dom has it, which says nothing of PDFs. */
export function restorePdfViewer(): void {
  Reflect.deleteProperty(navigator, 'pdfViewerEnabled');
}

/** A browser that makes elements full screen, which happy-dom is not: each element asked to go full screen, and how often it was left. */
export interface FullScreenStub {
  requested: Element[];
  exits: number;
  /** Makes `element` the one full screen, as the browser says. */
  show: (element: Element | null) => void;
  restore: () => void;
}

/** Makes the browser one that makes elements full screen; undo with `restore`. */
export function stubFullScreen(): FullScreenStub {
  let shown: Element | null = null;
  const request = Object.getOwnPropertyDescriptor(Element.prototype, 'requestFullscreen');
  const stub: FullScreenStub = {
    requested: [],
    exits: 0,
    show: (element) => {
      shown = element;
      document.dispatchEvent(new Event('fullscreenchange'));
    },
    restore: () => {
      for (const name of ['fullscreenEnabled', 'fullscreenElement', 'exitFullscreen']) Reflect.deleteProperty(document, name);
      if (request) Object.defineProperty(Element.prototype, 'requestFullscreen', request);
      else Reflect.deleteProperty(Element.prototype, 'requestFullscreen');
    },
  };
  Object.defineProperties(document, {
    fullscreenEnabled: { value: true, configurable: true },
    fullscreenElement: { get: () => shown, configurable: true },
    exitFullscreen: { value: async () => { stub.exits++; stub.show(null); }, configurable: true },
  });
  Object.defineProperty(Element.prototype, 'requestFullscreen', {
    value: async function (this: Element) {
      stub.requested.push(this);
      stub.show(this);
    },
    configurable: true,
  });
  return stub;
}
