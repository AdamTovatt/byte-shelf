import { useState, type ReactNode } from 'react';
import { useLoaded } from '../../hooks/useLoaded';
import type { ContentReader } from '../../lib/content/contentReader';
import { LoadedContent } from './LoadedContent';
import { PreviewFailure } from './PreviewFailure';
import { PreviewSpinner } from './PreviewSpinner';
import type { ShowFailure } from './showFailure';

interface UrlContentProps {
  content: ContentReader;
  /** Said when the contents can be read and the browser still cannot show them. */
  cannotShow: string;
  /** The element that loads the URL; it calls `onError` when it does not show what it loaded. */
  children: (url: string, onError: (failure: ShowFailure) => void) => ReactNode;
}

/** A file an element of the browser's shows by its URL, or why it does not. */
export function UrlContent({ content, cannotShow, children }: UrlContentProps) {
  const url = useLoaded(content, (signal) => content.url(signal));
  const [failure, setFailure] = useState<ShowFailure | null>(null);

  /** Loads a URL afresh: the one before may have stopped answering, as when the session's cookie ran out. */
  function tryAgain(): void {
    setFailure(null);
    url.reload();
  }

  if (failure === 'connectionLost') return <PreviewFailure message="The connection was lost." onTryAgain={tryAgain} />;
  if (failure === 'cannotShow') return <NotShown content={content} cannotShow={cannotShow} onTryAgain={tryAgain} />;
  return <LoadedContent loaded={url}>{(loaded) => children(loaded, setFailure)}</LoadedContent>;
}

/**
 * Why an element did not show a file: its contents could not be read, or else the browser cannot show them.
 * That is tried again too: a connection lost only as the element loaded them reads as this.
 */
function NotShown({ content, cannotShow, onTryAgain }: Pick<UrlContentProps, 'content' | 'cannotShow'> & { onTryAgain: () => void }) {
  const read = useLoaded(content, (signal) => content.range(0, 1, signal));
  if (read.status === 'loading') return <PreviewSpinner />;
  if (read.status === 'failed') return <PreviewFailure error={read.error} onTryAgain={onTryAgain} />;
  return <PreviewFailure message={cannotShow} onTryAgain={onTryAgain} />;
}
