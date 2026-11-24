import { useCallback, useEffect, useRef, useState } from 'react';
import { Icon, cn, getFileIcon } from '../ui';
import { useDerivedReader } from '../hooks/useDerivedReader';
import { useInView } from '../hooks/useInView';
import type { DerivedReader } from '../lib/derived/derivedReader';
import type { ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { RowIcon } from './RowIcon';

interface FileThumbnailProps {
  file: ShelfFile;
  /** Where its contents are read from, and what was made from them. */
  source: ContentSource;
}

/** A thumbnail's URL, as `reader` answered it, and whether what it loads is there to show. */
interface LoadedThumbnail {
  reader: DerivedReader;
  url: string;
  shown: boolean;
  /** Whether it was asked for anew, as it did not load the first time. */
  anew: boolean;
}

/** A file's thumbnail in a row, loaded as the row comes into view, and its type's icon until it is there or when there is none. */
export function FileThumbnail({ file, source }: FileThumbnailProps) {
  const slot = useRef<HTMLSpanElement>(null);
  const inView = useInView(slot);
  const version = file.derived?.thumbnail;
  const reader = useDerivedReader(inView && version !== undefined ? file : null, source, version);
  const [loaded, setLoaded] = useState<LoadedThumbnail | null>(null);

  const load = useCallback((from: DerivedReader, anew: boolean) => {
    from.url('thumbnail').then((url) => url !== null && setLoaded({ reader: from, url, shown: false, anew }), () => undefined);
  }, []);
  useEffect(() => {
    if (reader) load(reader, false);
  }, [reader, load]);

  function onError(failed: LoadedThumbnail): void {
    setLoaded(null);
    if (failed.anew) return;
    failed.reader.forget('thumbnail');
    load(failed.reader, true);
  }

  const current = loaded !== null && loaded.reader === reader ? loaded : null;
  return (
    <RowIcon ref={slot}>
      {!current?.shown && <Icon icon={getFileIcon(file.name)} size={18} className="text-muted" />}
      {current && (
        <img
          src={current.url}
          alt=""
          draggable={false}
          className={cn('size-8 rounded object-cover', !current.shown && 'hidden')}
          onLoad={() => setLoaded({ ...current, shown: true })}
          onError={() => onError(current)}
        />
      )}
    </RowIcon>
  );
}
