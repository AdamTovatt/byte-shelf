import { mediaTypeOf, type ViewerProps } from '@byteshelf/plugin';
import { LoadedAhead } from './LoadedAhead';
import { SharpeningImage } from './SharpeningImage';

const SVG = 'image/svg+xml';

/** An image that is there at once and then becomes sharp. An SVG or an encrypted one is the next viewer's, as one nothing was made of is. */
export function SharpeningViewer(props: ViewerProps) {
  const { file, neighbours, Fallback } = props;
  const type = mediaTypeOf(file.contentType);
  return (
    <>
      {file.encrypted || type === SVG ? <Fallback /> : <SharpeningImage {...props} />}
      {neighbours.filter((each) => mediaTypeOf(each.file.contentType).startsWith('image/')).map((each) => <LoadedAhead key={each.file.id} {...each} />)}
    </>
  );
}
