import { useState, type ReactNode } from 'react';
import { Button, Icon, IconButton, Modal, getErrorMessage, iconMap } from '../../ui';
import type { Steps } from '../../lib/preview/neighbours';
import { usePreviewContents } from '../../hooks/usePreviewContents';
import { useViewers } from '../../hooks/useViewers';
import { contentsKey } from '../../hooks/useWholeContents';
import { formatBytes } from '../../lib/format';
import { isTypingTarget } from '../../lib/keys';
import type { ReadFile } from '../../services/contentService';
import type { ShelfFile } from '../../services/shelfService';
import { ErrorBoundary } from '../ErrorBoundary';
import { FileDetailsDialog } from '../FileDetailsDialog';
import { fileOpening, opensInTab, type FileOpening } from '../fileOpening';
import { PreviewMessage } from './PreviewMessage';
import { PreviewSpinner } from './PreviewSpinner';
import { whyNotInPage } from './viewers/inPage';

interface FilePreviewProps {
  /** The file previewed and what reads it; null while its record loads. */
  previewed: ReadFile | null;
  /** Why its record could not be loaded. */
  error: Error | null;
  /** Its place among the files beside it; null when it is previewed alone. */
  steps: Steps | null;
  onClose: () => void;
  /** The shelf the file is live on, whose record of it Details shows; absent for one that is not. */
  detailsOn?: string;
  /** Whether a locked file's password is asked for as it shows, rather than once Unlock is pressed. */
  asksPassword: () => boolean;
  /** Dialogs over the preview, such as a password prompt. */
  children?: ReactNode;
}

/**
 * A file over its page, in the viewer that takes its type: its name, where it stands among the files
 * beside it (the arrow keys move between them), and saving it, opening it in a tab and its details.
 */
export function FilePreview({ previewed, error, steps, onClose, detailsOn, asksPassword, children }: FilePreviewProps) {
  const [detailing, setDetailing] = useState(false);
  const viewers = useViewers();
  const file = previewed?.file;
  const viewer = file ? viewers.viewerFor(file) : null;
  const notInPage = viewer && whyNotInPage(viewer);
  const { contents, reading } = usePreviewContents(viewer && !notInPage ? previewed : null, asksPassword);
  const opening = previewed && fileOpening(reading ?? previewed.reading);

  function onKeyDown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.defaultPrevented || ownsArrowKeys(event.target)) return;
    const step = event.key === 'ArrowLeft' ? steps?.toPrevious : event.key === 'ArrowRight' ? steps?.toNext : null;
    if (!step) return;
    event.preventDefault();
    step();
  }

  return (
    <Modal
      isOpen
      variant="fullscreen"
      title={file?.name ?? 'Preview'}
      onClose={onClose}
      onKeyDown={onKeyDown}
      disableContentScroll
      disableContentPadding
      headerActions={file && opening && (
        <div className="flex items-center gap-1 px-1">
          {steps && <div className="hidden sm:block"><StepButtons steps={steps} /></div>}
          <IconButton variant="ghost" size="sm" icon={iconMap.download} aria-label="Download" title="Download" onClick={() => void opening.save([file])} />
          {opensInTab(file) && <IconButton variant="ghost" size="sm" icon={iconMap.openInTab} aria-label="Open in new tab" title="Open in new tab" onClick={() => void opening.open(file)} />}
          {detailsOn !== undefined && <IconButton variant="ghost" size="sm" icon={iconMap.info} aria-label="Details" title="Details" onClick={() => setDetailing(true)} />}
        </div>
      )}
    >
      {/* The card does not clip what it holds, and a file may fill the preview to its corners. */}
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-b-xl">
        {file && opening ? shown(file, opening) : error ? <PreviewMessage message={getErrorMessage(error, 'Could not load the file.')} /> : <PreviewSpinner />}
        {steps && <div className="flex justify-center border-t border-border py-1 sm:hidden"><StepButtons steps={steps} /></div>}
      </div>
      {children}
      {file && detailing && detailsOn !== undefined && <FileDetailsDialog shelf={detailsOn} file={file} onClose={() => setDetailing(false)} />}
    </Modal>
  );

  function shown(of: ShelfFile, { open, save }: FileOpening): ReactNode {
    const download = <Button variant="secondary" icon={<Icon icon={iconMap.download} size={16} />} onClick={() => void save([of])}>Download</Button>;
    if (!viewer) return <PreviewMessage message="There is no preview of files of this type." action={download} />;

    if (notInPage) {
      const openInTab = opensInTab(of) && <Button variant="secondary" icon={<Icon icon={iconMap.openInTab} size={16} />} onClick={() => void open(of)}>Open in new tab</Button>;
      return <PreviewMessage message={notInPage} action={<div className="flex gap-2">{openInTab}{download}</div>} />;
    }

    if (contents.status === 'reading') return <PreviewSpinner />;
    if (contents.status === 'waiting') {
      return (
        <PreviewMessage
          message={`The whole file, ${formatBytes(of.sizeBytes)}, is read into the browser to show it.`}
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
      <ErrorBoundary key={`${contentsKey(of)} ${viewer.id}`} fallback={<PreviewMessage message="The viewer failed while showing this file." action={download} />}>
        <Viewer file={of} content={contents.reader} />
      </ErrorBoundary>
    );
  }
}

/** The previous and next file, around where this one stands. */
function StepButtons({ steps: { position, count, more, toPrevious, toNext } }: { steps: Steps }) {
  return (
    <div className="flex items-center gap-1">
      <IconButton variant="ghost" size="sm" icon={iconMap.chevronLeft} aria-label="Previous file" title="Previous file" disabled={!toPrevious} onClick={() => toPrevious?.()} />
      <span className="whitespace-nowrap text-body-sm text-muted">{position} of {count}{more && '+'}</span>
      <IconButton variant="ghost" size="sm" icon={iconMap.chevronRight} aria-label="Next file" title="Next file" disabled={!toNext} onClick={() => toNext?.()} />
    </div>
  );
}

/** Whether the arrow keys mean something to what has the focus, as they seek in a player and move in a field. */
function ownsArrowKeys(target: EventTarget | null): boolean {
  return isTypingTarget(target) || target instanceof HTMLMediaElement;
}
