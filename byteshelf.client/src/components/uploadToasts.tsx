import toast from 'react-hot-toast';
import { NameAndReason } from './MiddleTruncatedName';
import { uploadFailure } from './uploadWording';
import type { MissedState, Upload } from '../services/uploadQueue';

/** How long the toast stays; long enough to read the reason. */
const MISSED_MS = 8000;

/**
 * Tells of an upload whose name was taken or that failed while its folder is not shown, which its row would
 * otherwise tell unseen. It only says what happened: replacing and dismissing it stay on its row.
 */
export function toastMissedUpload(upload: Upload, state: MissedState): void {
  const reason = uploadFailure(state, upload);
  toast.error(<NameAndReason before="Could not upload" name={upload.name} reason={reason} />, { id: `upload-${upload.id}`, duration: MISSED_MS });
}
