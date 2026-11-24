import type { ReadableFile } from '../../services/fileReading';
import type { Steps } from './neighbours';

/** What an open preview shows. */
export interface ShownPreview {
  /** The file previewed and what reads it; null while its record loads. */
  previewed: ReadableFile | null;
  /** Why its record could not be loaded. */
  error: Error | null;
  /** Its place among the files beside it; null when it is previewed alone. */
  steps: Steps | null;
  /** The file before it, null when there is none. */
  previous: ReadableFile | null;
  /** The file after it, null when there is none or it is not fetched yet. */
  next: ReadableFile | null;
  onClose: () => void;
  /** Whether a locked file's password is asked for as it shows, rather than once Unlock is pressed. */
  asksPassword: boolean;
}

/** A preview over a page, and what it shows while it is open. */
export interface PagePreview {
  isOpen: boolean;
  shown: ShownPreview;
}
