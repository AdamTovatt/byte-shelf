import { Suspense, useState, type ReactNode } from 'react';
import { Button, Icon, IconButton, Modal, getErrorMessage, iconMap } from '../../ui';
import { contentsKey } from '../../lib/preview/fileContents';
import type { Steps } from '../../lib/preview/neighbours';
import type { ShownPreview } from '../../lib/preview/shownPreview';
import type { FileActions } from '../../hooks/useFileActions';
import { usePluginsAwaited } from '../../hooks/usePluginHost';
import { usePreviewContents } from '../../hooks/usePreviewContents';
import { useViewers } from '../../hooks/useViewers';
import { formatBytes } from '../../lib/format';
import { isTypingTarget } from '../../lib/keys';
import { openable, type OpenableFile } from '../../services/fileReading';
import { ErrorBoundary } from '../ErrorBoundary';
import { FileActionsMenu } from '../FileActionItems';
import { FileDetailsDialog } from '../FileDetailsDialog';
import { openFile, opensInTab, saveFiles } from '../fileOpening';
import { PreviewFailure } from './PreviewFailure';
import { PreviewMessage } from './PreviewMessage';
import { PreviewSpinner } from './PreviewSpinner';

interface FilePreviewProps extends ShownPreview {
  /** The shelf the file is live on, whose record of it Details shows; absent for one that is not. */
  detailsOn?: string;
  /** The plugins' actions, for a file that is live. */
  actions?: FileActions;
}

/**
 * A file over its page, in the viewer that takes its type: its name, where it stands among the files
 * beside it (the arrow keys move between them), and saving it, opening it in a tab and its details.
 */
export function FilePreview({ previewed, error, steps, onClose, detailsOn, actions, asksPassword }: FilePreviewProps) {
  const [detailing, setDetailing] = useState(false);
  const viewers = useViewers();
  const awaitsPlugins = usePluginsAwaited();
  const file = previewed?.file;
  const match = file ? viewers.viewerFor(file) : null;
  const { contents, readWhole } = usePreviewContents(match?.viewer ? previewed : null, asksPassword);
  const target = previewed && openable(previewed, readWhole ?? undefined);

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
      headerActions={target && (
        <div className="flex items-center gap-1 px-1">
          {steps && <div className="hidden sm:block"><StepButtons steps={steps} /></div>}
          <IconButton variant="ghost" size="sm" icon={iconMap.download} aria-label="Download" title="Download" onClick={() => void saveFiles([target])} />
          {opensInTab(target.file) && <IconButton variant="ghost" size="sm" icon={iconMap.openInTab} aria-label="Open in new tab" title="Open in new tab" onClick={() => void openFile(target)} />}
          {detailsOn !== undefined && <IconButton variant="ghost" size="sm" icon={iconMap.info} aria-label="Details" title="Details" onClick={() => setDetailing(true)} />}
          {actions && <FileActionsMenu actions={actions} target={target} />}
        </div>
      )}
    >
      {/* The card does not clip what it holds, and a file may fill the preview to its corners. */}
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-b-xl">
        {target ? shown(target) : error ? <PreviewMessage message={getErrorMessage(error, 'Could not load the file.')} /> : <PreviewSpinner />}
        {steps && <div className="flex justify-center border-t border-border py-1 sm:hidden"><StepButtons steps={steps} /></div>}
      </div>
      {file && detailing && detailsOn !== undefined && <FileDetailsDialog shelf={detailsOn} file={file} onClose={() => setDetailing(false)} />}
    </Modal>
  );

  function shown(of: OpenableFile): ReactNode {
    const download = <Button variant="secondary" icon={<Icon icon={iconMap.download} size={16} />} onClick={() => void saveFiles([of])}>Download</Button>;
    // A plugin still loading may show it, or show it better.
    if (awaitsPlugins) return <PreviewSpinner />;

    if (!match?.viewer) {
      const openInTab = opensInTab(of.file) && <Button variant="secondary" icon={<Icon icon={iconMap.openInTab} size={16} />} onClick={() => void openFile(of)}>Open in new tab</Button>;
      return <PreviewMessage message={match?.unavailable ?? 'There is no preview of files of this type.'} action={<div className="flex gap-2">{openInTab}{download}</div>} />;
    }

    if (contents.status === 'reading') return <PreviewSpinner />;
    if (contents.status === 'waiting') {
      return (
        <PreviewMessage
          message={`The whole file, ${formatBytes(of.file.sizeBytes)}, is read into the browser to show it.`}
          action={<Button onClick={contents.read}>Load</Button>}
        />
      );
    }
    if (contents.status === 'locked') {
      const message = contents.error === null ? 'This file is encrypted.' : getErrorMessage(contents.error, 'Could not unlock the file.');
      return <PreviewMessage message={message} action={<Button onClick={contents.unlock}>Unlock</Button>} />;
    }
    if (contents.status === 'failed') {
      return <PreviewFailure error={contents.error} onTryAgain={contents.retry} />;
    }

    const { id, component: Viewer } = match.viewer;
    return (
      <ErrorBoundary key={`${contentsKey(of.file)} ${id}`} fallback={<PreviewMessage message="The viewer failed while showing this file." action={download} />}>
        <Suspense fallback={<PreviewSpinner />}>
          <Viewer file={of.file} content={contents.reader} />
        </Suspense>
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
