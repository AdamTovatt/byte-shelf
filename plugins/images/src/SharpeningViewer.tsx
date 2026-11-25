import { mediaTypeOf, type ViewerProps } from '@byteshelf/plugin';
import { isImage } from './imageTypes';
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
      {/* The files beside it are any the arrow keys move to, whatever shows them. */}
      {neighbours.filter((each) => isImage(each.file.contentType)).map((each) => <LoadedAhead key={each.file.id} {...each} />)}
    </>
  );
}
