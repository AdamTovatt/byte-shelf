import type { ReactNode } from 'react';
import { CustomScroll } from '../../ui';
import { formatBytes } from '../../lib/format';
import type { ReadText } from '../../lib/content/readText';

/** What a text file holds, scrolling, however it is drawn: said to be only the start of it, of `cutAt` bytes, unless that is null. */
export function TextPane({ cutAt, children }: { cutAt: number | null; children: ReactNode }) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {cutAt !== null && <p className="border-b border-border px-5 py-2 text-body-sm text-muted">Only the first {formatBytes(cutAt)} is shown.</p>}
      <CustomScroll flex="1 1 auto" heightRelativeToParent="100%" className="min-h-0">{children}</CustomScroll>
    </div>
  );
}

/** A text file as it is written. */
export function PlainText({ text, cutAt }: ReadText) {
  return (
    <TextPane cutAt={cutAt}>
      <pre className="whitespace-pre-wrap break-words p-5 font-mono text-body-sm text-fg">{text}</pre>
    </TextPane>
  );
}
