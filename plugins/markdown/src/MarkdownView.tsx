import { useState } from 'react';
import { EmptyState, ErrorBoundary, PlainText, Tab, TabGroup, TextPane, cn, type ReadText } from '@byteshelf/plugin';
import { MarkdownDocument } from './MarkdownDocument';
import { renderedStart } from './renderedStart';

/** A Markdown file's text, as it reads or as it is written. */
export function MarkdownView({ text, cutAt }: ReadText) {
  const [showsSource, setShowsSource] = useState(false);
  const start = renderedStart({ text, cutAt });

  return (
    <div className="markdown-flex markdown-min-h-0 markdown-flex-1 markdown-flex-col">
      <TabGroup className="markdown-border-b markdown-border-border markdown-px-3">
        <Tab label="Preview" isActive={!showsSource} onClick={() => setShowsSource(false)} />
        <Tab label="Source" isActive={showsSource} onClick={() => setShowsSource(true)} />
      </TabGroup>
      {/* Kept while the source shows, rather than worked out again for coming back to it. */}
      <div hidden={showsSource} className={cn('markdown-min-h-0 markdown-flex-1 markdown-flex-col', !showsSource && 'markdown-flex')}>
        {/* Such as one nested too deep. A changed text is tried again. */}
        <ErrorBoundary key={start.text} fallback={<EmptyState message="This document could not be shown as it reads. Its source can." />}>
          <TextPane cutAt={start.cutAt}><MarkdownDocument text={start.text} /></TextPane>
        </ErrorBoundary>
      </div>
      {showsSource && <PlainText text={text} cutAt={cutAt} />}
    </div>
  );
}
