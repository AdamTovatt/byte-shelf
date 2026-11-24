import { LoadedContent, readText, useLoaded, type ViewerProps } from '@byteshelf/plugin';
import { MarkdownView } from './MarkdownView';

export default function MarkdownViewer({ file, content }: ViewerProps) {
  const loaded = useLoaded(content, (signal) => readText({ file, content }, signal));

  return <LoadedContent loaded={loaded}>{(read) => <MarkdownView {...read} />}</LoadedContent>;
}
