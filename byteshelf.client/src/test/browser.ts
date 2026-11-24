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
