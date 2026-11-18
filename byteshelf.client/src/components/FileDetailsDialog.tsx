import type { ReactNode } from 'react';
import { FormDialog } from './FormDialog';
import { useFileMetadata } from '../hooks/useShelfQueries';
import { formatBytes, formatDuration, formatTime } from '../lib/format';
import type { FileMetadata, ShelfFile } from '../services/shelfService';
import { getErrorMessage } from '../ui';

/** A file's record, and what the server read from its contents: when and where it was taken, the camera, its length, a PDF's title and pages. */
export function FileDetailsDialog({ shelf, file, onClose }: { shelf: string; file: ShelfFile; onClose: () => void }) {
  const metadata = useFileMetadata(shelf, file.id);

  return (
    <FormDialog open title={`Details of ${file.name}`} onClose={onClose}>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-body-sm">
        <Detail label="Type">{file.contentType}</Detail>
        <Detail label="Size">{formatBytes(file.sizeBytes)}</Detail>
        <Detail label="Stored">{formatTime(file.createdAt)}</Detail>
        <Detail label="Changed">{formatTime(file.updatedAt)}</Detail>
        {metadata.data && metadataDetails(metadata.data)}
        <Detail label="SHA-256"><span className="break-all font-mono">{file.sha256}</span></Detail>
      </dl>
      <p className="text-body-sm text-muted">{note(file, metadata.data, metadata.error)}</p>
    </FormDialog>
  );
}

function metadataDetails(metadata: FileMetadata): ReactNode {
  const { takenAt, latitude, longitude, durationSeconds, pageCount, title, camera } = metadata;
  return (
    <>
      {title && <Detail label="Title">{title}</Detail>}
      {pageCount != null && <Detail label="Pages">{pageCount}</Detail>}
      {takenAt && <Detail label="Taken">{formatTime(takenAt)}</Detail>}
      {latitude != null && longitude != null && <Detail label="Place">{`${formatDegrees(latitude)}, ${formatDegrees(longitude)}`}</Detail>}
      {camera && <Detail label="Camera">{camera}</Detail>}
      {durationSeconds != null && <Detail label="Length">{formatDuration(durationSeconds)}</Detail>}
    </>
  );
}

/** What the lines above leave unsaid about the contents: why there is nothing read from them, or that there is still to be. */
function note(file: ShelfFile, metadata: FileMetadata | undefined, error: Error | null): string {
  if (file.encrypted) return 'Encrypted: the server never reads its contents.';
  if (error) return `Could not load what its contents say: ${getErrorMessage(error, 'the server did not answer.')}`;
  if (!metadata) return 'Loading what its contents say…';
  if (metadata.readAt) return `Contents read ${formatTime(metadata.readAt)}.`;
  return metadata.readable ? 'Its contents have not been read yet.' : 'The server does not read files of this type.';
}

/** Degrees to six places, about a tenth of a metre, without trailing zeros. */
function formatDegrees(degrees: number): string {
  return String(Number(degrees.toFixed(6)));
}

function Detail({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className="min-w-0 text-fg">{children}</dd>
    </>
  );
}
