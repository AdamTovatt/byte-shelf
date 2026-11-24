import { ViewerRegistry } from '../../../lib/preview/viewerRegistry';
import { AUDIO } from './audio';
import { IMAGE } from './image';
import { PDF } from './pdf';
import { TEXT } from './text';
import { VIDEO } from './video';

/** The viewers that come with the app, registered as a plugin's are. The app reads them through `useViewers`. */
export const BUILT_IN_VIEWERS = ViewerRegistry.EMPTY.with(null, [IMAGE, VIDEO, AUDIO, PDF, TEXT]);
