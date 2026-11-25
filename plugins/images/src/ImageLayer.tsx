import { useEffect, useEffectEvent, useRef } from 'react';
import { cn } from '@byteshelf/plugin';
import type { ImageSize } from './recordedImage';

interface ImageLayerProps {
  /** Loads the layer into `image` and decodes it, as `loadImage` does; fails when there is none, or it does not load. */
  load: (image: HTMLImageElement, signal: AbortSignal) => Promise<void>;
  /** Its name while it is the layer that is seen; null until then. */
  alt: string | null;
  className?: string;
  fetchPriority?: 'high' | 'low';
  /** Told its size once it has been decoded, when the browser draws it whole at once. */
  onDecoded: (size: ImageSize) => void;
  onFailed: () => void;
}

/** One version of an image, in all of its box: unseen until it has an `alt`. */
export function ImageLayer({ load, alt, className, fetchPriority, onDecoded, onFailed }: ImageLayerProps) {
  const image = useRef<HTMLImageElement>(null);

  const loadInto = useEffectEvent(load);
  const decoded = useEffectEvent((element: HTMLImageElement) => onDecoded({ width: element.naturalWidth, height: element.naturalHeight }));
  const failed = useEffectEvent(onFailed);

  useEffect(() => {
    const element = image.current!;
    const loading = new AbortController();
    loadInto(element, loading.signal).then(
      () => decoded(element),
      () => {
        if (!loading.signal.aborted) failed();
      },
    );
    return () => loading.abort();
  }, []);

  return (
    <img
      ref={image}
      alt={alt ?? ''}
      draggable={false}
      // Off the page's own thread; loadImage awaits decode(), without which it would be shown partly decoded.
      decoding="async"
      fetchPriority={fetchPriority}
      className={cn('images-absolute images-inset-0 images-h-full images-w-full', alt === null && 'images-opacity-0', className)}
    />
  );
}
