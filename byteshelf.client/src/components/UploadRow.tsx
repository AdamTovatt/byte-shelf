import { Button, ErrorText, Icon, IconButton, ProgressBar, cn, getFileIcon, iconMap } from '../ui';
import { useUpload } from '../contexts/UploadHost';
import { formatBytes } from '../lib/format';
import { isMissed, isUnderway, type UnderwayState, type Upload } from '../services/uploadQueue';
import { FILE_ROW } from './fileRowLayout';
import { RowIcon } from './RowIcon';
import { uploadFailure } from './uploadFailure';

interface Stage {
  label: string;
  /** How far it is, as a percentage. */
  progress: number;
  /** Whether all is sent, and the server has not answered yet. */
  finishing: boolean;
}

/** Where an upload on its way is. */
function stage(state: UnderwayState): Stage {
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
 * An upload's row in the list of the folder it goes into, laid out as a file's row, so it reads as the file
 * that is coming: its stage and a bar where a file's size is, on a phone too, and Cancel until all is sent; a
 * taken name with Replace and Dismiss, a failure with its reason and Dismiss. One done is a file's row instead.
 * It is drawn again as its own upload moves on, apart from the list.
 */
export function UploadRow({ upload }: { upload: Upload }) {
  const { state, dismiss } = useUpload(upload.id);
  const { name } = upload;

  if (state && isUnderway(state)) {
    const shown = stage(state);
    return (
      <div data-upload-row={name} className={FILE_ROW.row}>
        <div className={FILE_ROW.main}>
          <UploadIcon name={name} />
          <span className={FILE_ROW.name}>{name}</span>
          <span className={cn(FILE_ROW.size, 'flex items-center gap-2')}>
            <ProgressBar size="sm" progress={shown.progress} label={`Uploading ${name}`} className="w-12 sm:w-24" />
            <span className="whitespace-nowrap tabular-nums">{shown.label}</span>
          </span>
        </div>
        {/* Hidden in its place once all is sent, as it could then no longer stop the file being stored. */}
        <IconButton
          variant="ghost"
          size="sm"
          icon={iconMap.close}
          aria-label={`Cancel uploading ${name}`}
          className={shown.finishing ? 'invisible' : undefined}
          onClick={state.cancel}
        />
      </div>
    );
  }

  if (!state || !isMissed(state)) return null;
  return (
    <div data-upload-row={name} className={FILE_ROW.row}>
      <div className={FILE_ROW.main}>
        <UploadIcon name={name} />
        <span className="flex min-w-0 flex-col">
          <span className={FILE_ROW.name}>{name}</span>
          <ErrorText>{uploadFailure(state, 'here')}</ErrorText>
        </span>
      </div>
      {state.status === 'taken' && <Button size="sm" variant="outlineDanger" onClick={state.replace}>Replace</Button>}
      <Button size="sm" variant="ghost" onClick={dismiss}>Dismiss</Button>
    </div>
  );
}

function UploadIcon({ name }: { name: string }) {
  return <RowIcon><Icon icon={getFileIcon(name)} size={18} className="text-muted" /></RowIcon>;
}
