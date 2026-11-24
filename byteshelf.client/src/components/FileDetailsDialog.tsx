import { useId, type ReactNode } from 'react';
import { FormDialog } from './FormDialog';
import { useFileMetadata } from '../hooks/useShelfQueries';
import { formatBytes, formatDuration, formatTime } from '../lib/format';
import type { FileMetadata, PluginFieldValue, PluginFileMetadata, ShelfFile } from '../services/shelfService';
import { getErrorMessage } from '../ui';

/**
 * A file's record, what the server read from its contents (when and where it was taken, the camera, its length,
 * a PDF's title and pages), and the fields plugins declare, under each plugin's name.
 */
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
      {Object.entries(metadata.data?.plugins ?? {}).map(([id, plugin]) => plugin.fields.length > 0 && <PluginDetails key={id} plugin={plugin} />)}
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

/** The declared fields of one plugin that have a value for the file, in the order it declares them. */
function PluginDetails({ plugin }: { plugin: PluginFileMetadata }) {
  const heading = useId();
  return (
    <section aria-labelledby={heading}>
      <h3 id={heading} className="mb-1.5 text-body-md text-fg">{plugin.name}</h3>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-body-sm">
        {plugin.fields.map((field) => (
          <Detail key={field.name} label={field.label}><div className="max-h-40 overflow-y-auto whitespace-pre-wrap break-words">{formatValue(field.value)}</div></Detail>
        ))}
      </dl>
    </section>
  );
}

function formatValue({ text, number, date, flag }: PluginFieldValue): string {
  if (text != null) return text;
  if (number != null) return String(number);
  if (date != null) return formatTime(date);
  return flag ? 'Yes' : 'No';
}

/** What the lines above leave unsaid about the contents: why there is nothing read from them, or that there is still to be. */
function note(file: ShelfFile, metadata: FileMetadata | undefined, error: Error | null): string {
  if (file.encrypted) {
    const unread = 'Encrypted: the server never reads its contents.';
    return error ? `${unread} Could not load what plugins keep about it: ${getErrorMessage(error, 'the server did not answer.')}` : unread;
  }

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
