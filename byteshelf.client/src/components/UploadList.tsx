import { Button, CardList, CardListItem, ProgressBar } from './ui';
import { formatBytes } from '../lib/format';
import type { UploadItem } from '../hooks/useUploads';

interface UploadListProps {
  items: UploadItem[];
  onReplace: (item: UploadItem) => void;
  onDismiss: (id: number) => void;
  onClearFinished: () => void;
}

/** The uploads started on this page: progress while they run, and what to do when one fails. */
export function UploadList({ items, onReplace, onDismiss, onClearFinished }: UploadListProps) {
  if (items.length === 0) return null;

  return (
    <section aria-label="Uploads" className="space-y-2">
      <div className="flex items-center justify-between">
        <h2 className="text-body-lg font-medium text-text-3-light dark:text-text-3-dark">Uploads</h2>
        {items.some((item) => item.status === 'done') && (
          <Button variant="text" size="sm" onClick={onClearFinished}>Clear finished</Button>
        )}
      </div>
      <CardList>
        {items.map((item) => (
          <CardListItem key={item.id}>
            <div className="space-y-1.5">
              <div className="flex items-center justify-between gap-3">
                <span className="truncate text-body-md text-text-1-light dark:text-text-1-dark">{item.path}</span>
                <span className="shrink-0 text-body-sm text-text-3-light dark:text-text-3-dark">{statusText(item)}</span>
              </div>
              {(item.status === 'uploading' || item.status === 'waiting') && (
                <ProgressBar value={item.file.size === 0 ? 0 : item.sentBytes / item.file.size} label={`Uploading ${item.file.name}`} />
              )}
              {(item.status === 'taken' || item.status === 'failed') && (
                <div className="flex items-center justify-between gap-3">
                  <span className="text-body-sm text-danger-light dark:text-danger-dark">{item.error}</span>
                  <span className="flex shrink-0 gap-2">
                    {item.status === 'taken' && <Button size="sm" variant="outlineDanger" onClick={() => onReplace(item)}>Replace</Button>}
                    <Button size="sm" variant="ghost" onClick={() => onDismiss(item.id)}>Dismiss</Button>
                  </span>
                </div>
              )}
            </div>
          </CardListItem>
        ))}
      </CardList>
    </section>
  );
}

function statusText(item: UploadItem): string {
  switch (item.status) {
    case 'waiting':
      return 'Waiting';
    case 'uploading':
      return `${formatBytes(item.sentBytes)} of ${formatBytes(item.file.size)}`;
    case 'done':
      return 'Stored';
    case 'taken':
      return 'Name taken';
    default:
      return 'Failed';
  }
}
