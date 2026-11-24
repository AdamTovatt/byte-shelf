import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { PdfViewer } from './PdfViewer';

/** PDFs. The browser shows one only by its type, so an untyped one has no viewer. */
export const PDF: Viewer = {
  id: 'pdf',
  mediaTypes: ['application/pdf'],
  extensions: [],
  component: PdfViewer,
};
