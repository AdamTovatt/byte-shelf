import { memo, type ComponentProps } from 'react';
import ReactMarkdown, { type Components, type ExtraProps } from 'react-markdown';
import remarkGfm from 'remark-gfm';

/** The addresses a link may lead to: the web's. A relative one would lead into the app. */
const WEB_ADDRESS = /^https?:\/\//i;

type Props<Tag extends keyof React.JSX.IntrinsicElements> = ComponentProps<Tag> & ExtraProps;

const components: Components = {
  a: ({ href, title, children }: Props<'a'>) => (
    href !== undefined && WEB_ADDRESS.test(href) ? <a href={href} title={title} target="_blank" rel="noopener noreferrer">{children}</a> : <>{children}</>
  ),
  // No image is loaded: one elsewhere would tell its server who reads the file.
  img: ({ alt, title }: Props<'img'>) => <span title={title}>{alt}</span>,
  // A table scrolls by itself, so a wide one never widens the page.
  table: ({ children }: Props<'table'>) => <div className="markdown-overflow-x-auto"><table>{children}</table></div>,
  // Not as remark writes it, hidden with a class of the app's.
  h2: ({ children }: Props<'h2'>) => <h2>{children}</h2>,
};

/**
 * Markdown as it reads, GitHub's tables, task lists and strikethrough included; HTML in it is left out.
 * Working out what it reads as takes a while for a long one, so only another text does it again.
 */
export const MarkdownDocument = memo(function MarkdownDocument({ text }: { text: string }) {
  return (
    <article className="markdown-document">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components} skipHtml>{text}</ReactMarkdown>
    </article>
  );
});
