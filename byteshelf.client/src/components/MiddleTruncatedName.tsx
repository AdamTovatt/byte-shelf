import { splitForMiddle } from '../lib/format';

/**
 * A file's or folder's name, or a path, that fits the width it is given by cutting out its middle,
 * as macOS does: "a-very-long-rep…ort-2026.pdf". The whole name is its tooltip, and its text, since
 * the cut is only drawn: a screen reader reads all of it. It shrinks as a flex item, so it goes in a
 * flex row such as {@link NameLine}; in running text it would keep its whole width.
 */
export function MiddleTruncatedName({ name }: { name: string }) {
  const { start, end } = splitForMiddle(name);
  return (
    <span title={name} className="inline-flex min-w-0 max-w-full">
      <span className="min-w-0 truncate">{start}</span>
      <span className="shrink-0 whitespace-pre">{end}</span>
    </span>
  );
}

/**
 * One line of a toast that names something: `before`, the name, then `after`, where only the name
 * gives way, cut in its middle, when the line does not fit the toast. The one way a toast shows a
 * name; anything long beside it, such as the server's reason, goes on a line of its own below.
 */
export function NameLine({ before, name, after }: { before?: string; name: string; after?: string }) {
  return (
    <span className="flex min-w-0 max-w-full">
      {before && <span className="shrink-0 whitespace-pre">{before} </span>}
      <MiddleTruncatedName name={name} />
      {after && <span className="shrink-0 whitespace-pre">{after}</span>}
    </span>
  );
}
