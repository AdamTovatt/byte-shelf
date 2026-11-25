import { useCallback, useState } from 'react';
import type { OpenFolder } from '../lib/openFolder';
import { runOrToast } from '../lib/utils';
import { uploadQueue, type UploadReporter } from '../services/uploadQueue';
import { usePasswordPrompt } from './usePasswordPrompt';
import { useInvalidateShelf } from './useShelfQueries';

/** Uploads `files` into `folder`, as it was read where this was started, as `QueueUploads` says. */
export type UploadFiles = (shelf: string, folder: OpenFolder, files: File[]) => void;

/** A queue of uploads, telling `report` how each goes, which asks for passwords until what uses it is gone. */
export function useUploadQueue(report: UploadReporter): UploadFiles {
  const askFirst = usePasswordPrompt();
  const ask = usePasswordPrompt();
  const changed = useInvalidateShelf();
  const [queueUploads] = useState(() => uploadQueue({ askFirst, ask, report, changed }));

  return useCallback((shelf, folder, files) => void runOrToast(() => queueUploads(shelf, folder, files), 'Could not upload'), [queueUploads]);
}
