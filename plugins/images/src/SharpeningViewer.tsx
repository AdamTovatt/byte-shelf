import { ImageViewer, mediaTypeOf, type ViewerProps } from '@byteshelf/plugin';
import { LoadedAhead } from './LoadedAhead';
import { SharpeningImage } from './SharpeningImage';

const SVG = 'image/svg+xml';

/** An image that is there at once and then becomes sharp. One nothing is made of is shown as the app shows it. */
export function SharpeningViewer(props: ViewerProps) {
  const { file, neighbours } = props;
  const type = mediaTypeOf(file.contentType);
  return (
    <>
      {file.encrypted || type === SVG ? <ImageViewer {...props} /> : <SharpeningImage {...props} />}
      {neighbours.filter((each) => mediaTypeOf(each.file.contentType).startsWith('image/')).map((each) => <LoadedAhead key={each.file.id} {...each} />)}
    </>
  );
}
