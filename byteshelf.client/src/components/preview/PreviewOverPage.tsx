import type { ReactNode } from 'react';
import type { PagePreview } from '../../lib/preview/shownPreview';
import { FilePreview } from './FilePreview';

interface PreviewOverPageProps {
  preview: PagePreview;
  /** The shelf the files previewed are live on; absent for those that are not. */
  detailsOn?: string;
  /** A dialog of the page's, such as a password prompt: inside the preview while it is open, so the page is dimmed once. */
  children: ReactNode;
}

/** A page's preview while it is open, and the dialog that stands over either. */
export function PreviewOverPage({ preview, detailsOn, children }: PreviewOverPageProps) {
  return preview.isOpen ? <FilePreview {...preview.shown} detailsOn={detailsOn}>{children}</FilePreview> : children;
}
