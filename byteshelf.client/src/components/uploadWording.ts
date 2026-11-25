import { getErrorMessage } from '../ui';
import { formatBytes } from '../lib/format';
import { ROOT_PATH } from '../lib/shelfPaths';
import type { MissedState, UnderwayState, Upload } from '../services/uploadQueue';

/** What an upload's row shows of an upload on its way. */
export interface UploadStage {
  label: string;
  /** How far it is, as a percentage. */
  progress: number;
  /** Whether all is sent, and the server has not answered yet. */
  finishing: boolean;
}

/**
 * Where an upload on its way is, as its row shows it. One sent again once its sign-in was renewed starts over
 * from none, as all of it really is sent again, and Cancel can stop that.
 */
export function uploadStage(state: UnderwayState): UploadStage {
  switch (state.status) {
    case 'waiting':
      return { label: 'Waiting', progress: 0, finishing: false };
    case 'encrypting':
      return { label: 'Encrypting', progress: 0, finishing: false };
    case 'uploading': {
      const { sentBytes, totalBytes } = state;
      if (sentBytes >= totalBytes) return { label: 'Finishing', progress: 100, finishing: true };
      return { label: `${formatBytes(sentBytes)} of ${formatBytes(totalBytes)}`, progress: (sentBytes / totalBytes) * 100, finishing: false };
    }
  }
}

/**
 * Why an upload whose name was taken or that failed was not stored. `elsewhere` is the upload where it is told
 * apart from its folder, which it names (a shelf's root by the shelf's name, as the breadcrumb does); null on its
 * row, which is in that folder's list, so it says "here".
 */
export function uploadFailure(state: MissedState, elsewhere: Upload | null): string {
  if (state.status === 'failed') return getErrorMessage(state.error, 'The upload failed');
  const place = !elsewhere ? 'here' : `in ${elsewhere.folderPath === ROOT_PATH ? elsewhere.shelf : elsewhere.folderPath}`;
  return state.failure === undefined
    ? `A file with this name is already ${place}.`
    : `Could not replace the file ${place}: ${getErrorMessage(state.failure, 'it was refused')}`;
}
