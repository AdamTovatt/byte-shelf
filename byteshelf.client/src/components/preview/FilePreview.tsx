import { useMemo, useState, type ReactNode } from 'react';
import { Button, Icon, IconButton, Modal, getErrorMessage, iconMap } from '../../ui';
import type { DerivedReader } from '../../lib/derived/derivedReader';
import { BackdropClick } from '../../lib/preview/backdropClick';
import type { Steps } from '../../lib/preview/neighbours';
import type { ShownPreview } from '../../lib/preview/shownPreview';
import type { Neighbour } from '../../lib/preview/viewerRegistry';
import type { FileActions } from '../../hooks/useFileActions';
import { useDerivedReaders, type DerivedOf } from '../../hooks/useDerivedReaders';
import { useSwipe } from '../../hooks/useSwipe';
import { usePluginsAwaited } from '../../hooks/usePluginHost';
import { usePreviewContents } from '../../hooks/usePreviewContents';
import { useViewers } from '../../hooks/useViewers';
import { formatBytes } from '../../lib/format';
import { isTypingTarget } from '../../lib/keys';
import { openable, type OpenableFile, type ReadableFile } from '../../services/fileReading';
import type { ShelfFile } from '../../services/shelfService';
import { FileActionsMenu } from '../FileActionItems';
import { FileDetailsDialog } from '../FileDetailsDialog';
import { openFile, opensInTab, saveFiles } from '../fileOpening';
import { FloatingBars } from './FloatingBars';
import { PreviewFailure } from './PreviewFailure';
import { PreviewMessage } from './PreviewMessage';
import { PreviewSpinner } from './PreviewSpinner';
import { ViewerChain } from './ViewerChain';

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
export function FilePreview({ previewed, error, steps, previous, next, onClose, detailsOn, actions, asksPassword }: FilePreviewProps) {
  const [detailing, setDetailing] = useState(false);
  const viewers = useViewers();
  const awaitsPlugins = usePluginsAwaited();
  const file = previewed?.file;
  const match = file ? viewers.viewerFor(file) : null;
  const shownBy = match?.viewers.length ? match.viewers : null;
  const { contents, readWhole } = usePreviewContents(shownBy ? previewed : null, asksPassword);
  const [derived, previousDerived, nextDerived] = useDerivedReaders([shownBy ? derivedOf(previewed) : null, derivedOf(previous), derivedOf(next)]);
  const neighbours = useMemo(() => [...neighbour(previous?.file, previousDerived), ...neighbour(next?.file, nextDerived)], [previous?.file, previousDerived, next?.file, nextDerived]);
  const target = previewed && openable(previewed, readWhole ?? undefined);
  const swipe = useSwipe({ onLeft: steps?.toNext, onRight: steps?.toPrevious });

  function onKeyDown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.defaultPrevented || ownsArrowKeys(event.target)) return;
    const step = event.key === 'ArrowLeft' ? steps?.toPrevious : event.key === 'ArrowRight' ? steps?.toNext : null;
    if (!step) return;
    event.preventDefault();
    step();
  }

  const title = file?.name ?? 'Preview';

  return (
    <Modal isOpen variant="fullscreen" title={title} onClose={onClose} onKeyDown={onKeyDown}>
      <FloatingBars
        title={title}
        steps={steps && <StepButtons steps={steps} />}
        actions={(
          <>
            {target && (
              <>
                <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.download} aria-label="Download" title="Download" onClick={() => void saveFiles([target])} />
                {opensInTab(target.file) && <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.openInTab} aria-label="Open in new tab" title="Open in new tab" onClick={() => void openFile(target)} />}
                {detailsOn !== undefined && <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.info} aria-label="Details" title="Details" onClick={() => setDetailing(true)} />}
                {actions && <FileActionsMenu actions={actions} target={target} />}
              </>
            )}
            <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.close} aria-label="Close" title="Close" onClick={onClose} />
          </>
        )}
      >
        <div className="flex min-h-0 flex-1 flex-col" {...swipe}>
          <BackdropClick.Provider value={onClose}>
            {target ? shown(target) : error ? <PreviewMessage message={getErrorMessage(error, 'Could not load the file.')} /> : <PreviewSpinner />}
          </BackdropClick.Provider>
        </div>
      </FloatingBars>
      {/* Beside the bars, not in what they float over: what is done in a dialog over the preview is not done to them. */}
      {file && detailing && detailsOn !== undefined && <FileDetailsDialog shelf={detailsOn} file={file} onClose={() => setDetailing(false)} />}
    </Modal>
  );

  function shown(of: OpenableFile): ReactNode {
    const download = <Button variant="secondary" icon={<Icon icon={iconMap.download} size={16} />} onClick={() => void saveFiles([of])}>Download</Button>;
    // A plugin still loading may show it, or show it better.
    if (awaitsPlugins) return <PreviewSpinner />;

    const openInTab = opensInTab(of.file) && <Button variant="secondary" icon={<Icon icon={iconMap.openInTab} size={16} />} onClick={() => void openFile(of)}>Open in new tab</Button>;
    const noPreview = (message: string) => <PreviewMessage message={message} action={<div className="flex gap-2">{openInTab}{download}</div>} />;
    if (!shownBy || !derived) return noPreview(match?.unavailable ?? 'There is no preview of files of this type.');

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

    return (
      <ViewerChain
        viewers={shownBy}
        file={of.file}
        content={contents.reader}
        derived={derived}
        neighbours={neighbours}
        failed={<PreviewMessage message="The viewer failed while showing this file." action={download} />}
        none={noPreview(match?.unavailable ?? 'No other viewer shows this file.')}
      />
    );
  }
}

function derivedOf(readable: ReadableFile | null): DerivedOf | null {
  return readable && { file: readable.file, source: readable.reading.source };
}

function neighbour(file: ShelfFile | undefined, derived: DerivedReader | null): Neighbour[] {
  return file && derived ? [{ file, derived }] : [];
}

/** The previous and next file, around where this one stands. */
function StepButtons({ steps: { position, count, more, toPrevious, toNext } }: { steps: Steps }) {
  return (
    <div className="flex items-center gap-1">
      <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.chevronLeft} aria-label="Previous file" title="Previous file" disabled={!toPrevious} onClick={() => toPrevious?.()} />
      <span className="whitespace-nowrap text-body-sm text-muted">{position} of {count}{more && '+'}</span>
      <IconButton variant="ghost" size="sm" shape="pill" icon={iconMap.chevronRight} aria-label="Next file" title="Next file" disabled={!toNext} onClick={() => toNext?.()} />
    </div>
  );
}

/** Whether the arrow keys mean something to what has the focus, as they seek in a player and move in a field. */
function ownsArrowKeys(target: EventTarget | null): boolean {
  return isTypingTarget(target) || target instanceof HTMLMediaElement;
}
