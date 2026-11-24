import { useState, type ReactNode } from 'react';
import { Button, Icon, IconButton, Modal, getErrorMessage, iconMap } from '../../ui';
import type { Neighbours } from '../../lib/preview/neighbours';
import { contentsKey, usePreviewContents } from '../../hooks/usePreviewContents';
import { formatBytes } from '../../lib/format';
import { isTypingTarget } from '../../lib/keys';
import type { FileReading } from '../../services/contentService';
import type { ShelfFile } from '../../services/shelfService';
import { ErrorBoundary } from '../ErrorBoundary';
import { FileDetailsDialog } from '../FileDetailsDialog';
import { fileOpening, opensInTab } from '../fileOpening';
import { PreviewMessage } from './PreviewMessage';
import { PreviewSpinner } from './PreviewSpinner';
import { viewers } from './viewers';

interface FilePreviewProps {
  /** The file previewed; undefined while its record loads. */
  file: ShelfFile | undefined;
  /** Why its record could not be loaded. */
  error: Error | null;
  reading: FileReading;
  /** The files before and after it; null when it is previewed alone. */
  neighbours: Neighbours | null;
  onMoveTo: (file: ShelfFile) => void;
  /** Given while more files follow the ones `neighbours` counts: finds the next of them and moves to it. */
  onMoveFurther?: () => void;
  onClose: () => void;
  /** The shelf the file is live on, whose record of it Details shows; absent for one that is not. */
  detailsOn?: string;
}

/**
 * A file over its page, in the viewer that takes its type: its name, where it stands among the files
 * beside it (the arrow keys move between them), and saving it, opening it in a tab and its details.
 */
export function FilePreview({ file, error, reading, neighbours, onMoveTo, onMoveFurther, onClose, detailsOn }: FilePreviewProps) {
  const [detailing, setDetailing] = useState(false);
  const viewer = file ? viewers.viewerFor(file) : null;
  const contents = usePreviewContents(viewer ? file : undefined, reading);
  // What is held already is saved or opened as it is, rather than read again.
  const opening = fileOpening({ ...reading, contents: async (each) => (contents.status === 'ready' && contents.held) || reading.contents(each) });
  const toPrevious = neighbours?.previous && (() => onMoveTo(neighbours.previous!));
  const toNext = neighbours?.next ? () => onMoveTo(neighbours.next!) : onMoveFurther;

  function onKeyDown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.defaultPrevented || ownsArrowKeys(event.target)) return;
    const step = event.key === 'ArrowLeft' ? toPrevious : event.key === 'ArrowRight' ? toNext : null;
    if (!step) return;
    event.preventDefault();
    step();
  }

  const steps = neighbours && (
    <div className="flex items-center gap-1">
      <IconButton variant="ghost" size="sm" icon={iconMap.chevronLeft} aria-label="Previous file" title="Previous file" disabled={!toPrevious} onClick={() => toPrevious?.()} />
      <span className="whitespace-nowrap text-body-sm text-muted">{neighbours.position} of {neighbours.count}{onMoveFurther && '+'}</span>
      <IconButton variant="ghost" size="sm" icon={iconMap.chevronRight} aria-label="Next file" title="Next file" disabled={!toNext} onClick={() => toNext?.()} />
    </div>
  );

  return (
    <Modal
      isOpen
      variant="fullscreen"
      title={file?.name ?? 'Preview'}
      onClose={onClose}
      onKeyDown={onKeyDown}
      disableContentScroll
      disableContentPadding
      headerActions={file && (
        <div className="flex items-center gap-1 px-1">
          {steps && <div className="hidden sm:block">{steps}</div>}
          <IconButton variant="ghost" size="sm" icon={iconMap.download} aria-label="Download" title="Download" onClick={() => void opening.save([file])} />
          {opensInTab(file) && <IconButton variant="ghost" size="sm" icon={iconMap.openInTab} aria-label="Open in new tab" title="Open in new tab" onClick={() => void opening.open(file)} />}
          {detailsOn !== undefined && <IconButton variant="ghost" size="sm" icon={iconMap.info} aria-label="Details" title="Details" onClick={() => setDetailing(true)} />}
        </div>
      )}
    >
      {/* The card does not clip what it holds, and a file may fill the preview to its corners. */}
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-b-xl">
        {file ? shown(file) : error ? <PreviewMessage message={getErrorMessage(error, 'Could not load the file.')} /> : <PreviewSpinner />}
        {steps && <div className="flex justify-center border-t border-border py-1 sm:hidden">{steps}</div>}
      </div>
      {file && detailing && detailsOn !== undefined && <FileDetailsDialog shelf={detailsOn} file={file} onClose={() => setDetailing(false)} />}
    </Modal>
  );

  function shown(previewed: ShelfFile): ReactNode {
    const download = <Button variant="secondary" icon={<Icon icon={iconMap.download} size={16} />} onClick={() => void opening.save([previewed])}>Download</Button>;
    if (!viewer) return <PreviewMessage message="There is no preview of files of this type." action={download} />;

    if (contents.status === 'reading') return <PreviewSpinner />;
    if (contents.status === 'waiting') {
      return (
        <PreviewMessage
          message={`The whole file, ${formatBytes(previewed.sizeBytes)}, is read into the browser to show it.`}
          action={<Button onClick={contents.read}>Load</Button>}
        />
      );
    }
    if (contents.status === 'locked') return <PreviewMessage message="This file is encrypted." action={<Button onClick={contents.unlock}>Unlock</Button>} />;
    if (contents.status === 'failed') {
      return <PreviewMessage message={getErrorMessage(contents.error, 'Could not read the file.')} action={<Button variant="secondary" onClick={contents.retry}>Try again</Button>} />;
    }

    const Viewer = viewer.component;
    return (
      <ErrorBoundary key={`${contentsKey(previewed)} ${viewer.id}`} fallback={<PreviewMessage message="The viewer failed while showing this file." action={download} />}>
        <Viewer file={previewed} content={contents.reader} />
      </ErrorBoundary>
    );
  }
}

/** Whether the arrow keys mean something to what has the focus, as they seek in a player and move in a field. */
function ownsArrowKeys(target: EventTarget | null): boolean {
  return isTypingTarget(target) || target instanceof HTMLMediaElement;
}
