import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { PdfViewer } from './PdfViewer';

/** PDFs. The browser shows one only by its type, so an untyped one has no viewer. */
export const PDF: Viewer = {
  id: 'pdf',
  mediaTypes: ['application/pdf'],
  extensions: [],
  component: PdfViewer,
  // Most phones show no PDF inside a page, only in a tab.
  unavailable: () => (navigator.pdfViewerEnabled === false ? 'This browser does not show a PDF inside a page.' : null),
  // Its toolbar is the browser's, along its top.
  clearOfBars: true,
};
