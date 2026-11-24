import { useEffect, useEffectEvent, useRef } from 'react';
import { cn } from '@byteshelf/plugin';
import { loadImage } from './loadImage';
import type { ImageSize } from './recordedImage';

/** Where a layer is loaded from. */
export interface LayerAddress {
  url: string;
  /** Has the address asked for anew, for one that may have gone. */
  forget?: () => void;
}

interface ImageLayerProps {
  /** Answers where it is loaded from; null when there is none. Asked again once when what it answers does not load and can be forgotten. */
  address: (signal: AbortSignal) => Promise<LayerAddress | null>;
  /** Its name while it is the layer that is seen; null until then. */
  alt: string | null;
  className?: string;
  fetchPriority?: 'high' | 'low';
  /** Told its size once it has been decoded, when the browser draws it whole at once. */
  onDecoded: (size: ImageSize) => void;
  onFailed: () => void;
}

/** One version of an image, in all of its box: unseen until it has an `alt`. */
export function ImageLayer({ address, alt, className, fetchPriority, onDecoded, onFailed }: ImageLayerProps) {
  const image = useRef<HTMLImageElement>(null);

  const load = useEffectEvent(async (element: HTMLImageElement, signal: AbortSignal) => {
    for (let isFirst = true; ; isFirst = false) {
      const found = await address(signal);
      if (!found) throw new Error('There is none.');
      try {
        return await loadImage(element, found.url, signal);
      } catch (error) {
        if (!isFirst || !found.forget || signal.aborted) throw error;
        found.forget();
      }
    }
  });
  const decoded = useEffectEvent((element: HTMLImageElement) => onDecoded({ width: element.naturalWidth, height: element.naturalHeight }));
  const failed = useEffectEvent(onFailed);

  useEffect(() => {
    const element = image.current!;
    const loading = new AbortController();
    load(element, loading.signal).then(
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
