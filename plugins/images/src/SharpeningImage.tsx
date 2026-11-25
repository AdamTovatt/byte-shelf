import { useState, type CSSProperties } from 'react';
import { Spinner, cn, mediaTypeOf, usePluginDocuments, useZoom, type DerivedKind, type ViewerProps } from '@byteshelf/plugin';
import { ImageLayer, type LayerAddress } from './ImageLayer';
import { recordedImage, type ImageSize } from './recordedImage';
import { showsType } from './showsType';

/** What an image is shown by after its placeholder, each sharper than the one before. */
const LAYERS = ['thumbnail', 'preview', 'whole'] as const;
type LayerName = (typeof LAYERS)[number];

function isSharper(one: LayerName | null, other: LayerName): boolean {
  return one !== null && LAYERS.indexOf(one) > LAYERS.indexOf(other);
}

/**
 * An image in a box of its size from the start, shown by the sharpest of its placeholder, thumbnail, preview
 * and the whole of it that has been loaded and decoded. One takes the place of another between two frames, under
 * the same zoom. When there is nothing to show but the whole, it is the next viewer's to show, or to say why not.
 */
export function SharpeningImage(props: ViewerProps) {
  const { file, content, derived, Fallback } = props;
  const { frame, zoomed: { ref: box, style: zoom } } = useZoom<HTMLDivElement>();
  const documents = usePluginDocuments(file);
  const recorded = recordedImage(documents.data?.contents);
  /** As recorded or, while that is not known, as first decoded. Once it is known it stays: the image does not jump. */
  const [size, setSize] = useState<ImageSize | null>(null);
  const [shown, setShown] = useState<LayerName | null>(null);
  const [failed, setFailed] = useState<readonly LayerName[]>([]);
  /** Whether there was a preview from the start, which is worth loading before the whole. */
  const [hadPreview] = useState(() => derived.has('preview'));
  const [isHandedOver, setIsHandedOver] = useState(false);
  if (recorded && !size) setSize(recorded);

  const hasFailed = (name: LayerName) => failed.includes(name);
  const isPreviewDone = hasFailed('preview') || shown === 'preview' || shown === 'whole';

  const hasNothingMade = hasFailed('preview') && !documents.isPending && !recorded && size === null;
  const hasNothingToShow = hasFailed('preview') && hasFailed('whole') && (shown === null || shown === 'thumbnail');
  if ((hasNothingMade || hasNothingToShow) && !isHandedOver) setIsHandedOver(true);
  if (isHandedOver) return <Fallback />;

  function layer(name: LayerName, address: (signal: AbortSignal) => Promise<LayerAddress | null>, look: { className?: string; fetchPriority?: 'high' | 'low' }) {
    if (isSharper(shown, name) || hasFailed(name)) return null;
    return (
      <ImageLayer
        key={name}
        address={address}
        alt={shown === name ? file.name : null}
        {...look}
        onDecoded={(natural) => {
          setSize((before) => before ?? natural);
          setShown((before) => (isSharper(before, name) ? before : name));
        }}
        onFailed={() => setFailed((before) => [...before, name])}
      />
    );
  }

  async function derivedAddress(kind: DerivedKind, signal: AbortSignal): Promise<LayerAddress | null> {
    const url = await derived.url(kind, signal);
    return url === null ? null : { url, forget: () => derived.forget(kind) };
  }

  /** The contents themselves or, where the browser does not show their type, what was made of them for that. */
  async function wholeAddress(signal: AbortSignal): Promise<LayerAddress | null> {
    const isShown = await showsType(mediaTypeOf(file.contentType));
    return (isShown ? null : await derivedAddress('full', signal)) ?? { url: await content.url(signal) };
  }

  return (
    <div {...frame}>
      <div className="images-stage images-flex images-min-w-0 images-flex-1 images-items-center images-justify-center images-self-stretch">
        {!size && <Spinner size="lg" />}
        <div
          ref={box}
          className={size ? 'images-box images-relative images-shrink-0 images-overflow-hidden' : 'images-hidden'}
          style={size ? { ...zoom, '--images-width': size.width, '--images-height': size.height } as CSSProperties : undefined}
        >
          {recorded && shown === null && <Placeholder url={recorded.placeholder} />}
          {size && (shown === 'thumbnail' || derived.has('thumbnail')) && layer('thumbnail', (signal) => derivedAddress('thumbnail', signal), {})}
          {layer('preview', (signal) => derivedAddress('preview', signal), { fetchPriority: 'high' })}
          {(!hadPreview || isPreviewDone) && layer('whole', wholeAddress, { className: 'images-object-contain', fetchPriority: 'low' })}
        </div>
      </div>
    </div>
  );
}

/** A tiny copy of an image over all of its box, blurred, and as it is under the edges, where the blur lets through what is below. */
function Placeholder({ url }: { url: string }) {
  return [false, true].map((isBlurred) => (
    <img key={String(isBlurred)} src={url} alt="" draggable={false} decoding="sync" className={cn('images-absolute images-inset-0 images-h-full images-w-full', isBlurred && 'images-blur-xl')} />
  ));
}
