import { useEffect, useRef, useState } from 'react';
import { Icon, cn, getFileIcon } from '../ui';
import { useDerivedReader } from '../hooks/useDerivedReader';
import { useInView } from '../hooks/useInView';
import { loadDerived, type DerivedReader } from '../lib/derived/derivedReader';
import { loadImage } from '../lib/loadImage';
import type { ContentSource } from '../services/contentService';
import type { ShelfFile } from '../services/shelfService';
import { RowIcon } from './RowIcon';

interface FileThumbnailProps {
  file: ShelfFile;
  /** Where its contents are read from, and what was made from them. */
  source: ContentSource;
}

/** A file's thumbnail in a row, loaded as the row comes into view, and its type's icon until it is there or when there is none. */
export function FileThumbnail({ file, source }: FileThumbnailProps) {
  const slot = useRef<HTMLSpanElement>(null);
  const image = useRef<HTMLImageElement>(null);
  const inView = useInView(slot);
  const version = file.derived?.thumbnail;
  const reader = useDerivedReader(inView && version !== undefined ? file : null, source, version);
  /** The reader whose thumbnail was loaded, which is shown while it is the row's. */
  const [loadedBy, setLoadedBy] = useState<DerivedReader | null>(null);

  useEffect(() => {
    if (!reader) return;
    const element = image.current!;
    const loading = new AbortController();
    loadDerived(reader, 'thumbnail', (url) => loadImage(element, url, loading.signal), loading.signal).then(
      (found) => found !== null && setLoadedBy(reader),
      () => undefined,
    );
    return () => loading.abort();
  }, [reader]);

  const isShown = reader !== null && loadedBy === reader;
  return (
    <RowIcon ref={slot}>
      {!isShown && <Icon icon={getFileIcon(file.name)} size={18} className="text-muted" />}
      {reader && <img ref={image} alt="" draggable={false} className={cn('size-8 rounded object-cover', !isShown && 'hidden')} />}
    </RowIcon>
  );
}
