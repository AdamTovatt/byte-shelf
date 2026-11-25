import { getErrorMessage } from '../ui';
import type { MissedState } from '../services/uploadQueue';

/**
 * Why an upload whose name was taken or that failed was not stored. `place` says where its folder is: "here"
 * on its row, which is in that folder's list.
 */
export function uploadFailure(state: MissedState, place: string): string {
  if (state.status === 'failed') return getErrorMessage(state.error, 'The upload failed');
  return state.failure === undefined
    ? `A file with this name is already ${place}.`
    : `Could not replace the file ${place}: ${getErrorMessage(state.failure, 'it was refused')}`;
}
