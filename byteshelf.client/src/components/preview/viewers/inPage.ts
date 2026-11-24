import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { PDF } from './pdf';

/**
 * Why this browser does not show a file inside the page with `viewer`; null when it does. Most phones show
 * no PDF there, only in a tab: one opens in a tab instead, and reached by previous or next it says so. A
 * plugin's own viewer of PDFs needs nothing of the browser's.
 */
export function whyNotInPage(viewer: Viewer): string | null {
  return viewer === PDF && navigator.pdfViewerEnabled === false ? 'This browser does not show a PDF inside a page.' : null;
}
