import toast from 'react-hot-toast';
import { Button, ProgressBar } from '../ui';
import { formatBytes } from '../lib/format';
import type { UploadReporter } from '../hooks/useUploads';

/** How long a failed upload's toast stays; long enough to read the reason. */
const FAILED_MS = 8000;

/**
 * Shows each upload as one toast, updated in place by its id: progress while it runs, a short success,
 * and an error toast when it fails. A taken name stays until Replace or Dismiss.
 */
export const reportUpload: UploadReporter = (upload, state) => {
  const id = `upload-${upload.id}`;
  const name = upload.file.name;

  switch (state.status) {
    case 'uploading': {
      const size = upload.file.size;
      toast.loading(
        <span className="flex w-56 min-w-0 flex-col gap-1.5">
          <span className="flex justify-between gap-3">
            <span className="truncate">{name}</span>
            <span className="shrink-0 text-body-sm text-muted">{formatBytes(state.sentBytes)} of {formatBytes(size)}</span>
          </span>
          <ProgressBar size="sm" progress={size === 0 ? 0 : (state.sentBytes / size) * 100} label={`Uploading ${name}`} />
        </span>,
        { id },
      );
      return;
    }
    case 'done':
      toast.success(`Uploaded ${name}`, { id });
      return;
    case 'taken':
      toast.error((t) => (
        <span className="flex flex-col gap-2">
          <span>{state.error}</span>
          <span className="flex gap-2">
            <Button size="sm" variant="outlineDanger" onClick={() => { toast.dismiss(t.id); state.replace(); }}>Replace</Button>
            <Button size="sm" variant="ghost" onClick={() => toast.dismiss(t.id)}>Dismiss</Button>
          </span>
        </span>
      ), { id, duration: Infinity });
      return;
    case 'failed':
      toast.error(`Could not upload ${name}: ${state.error}`, { id, duration: FAILED_MS });
      return;
  }
};
