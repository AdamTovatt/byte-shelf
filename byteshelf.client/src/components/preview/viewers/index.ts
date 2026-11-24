import { ViewerRegistry } from '../../../lib/preview/viewerRegistry';
import { IMAGE } from './image';
import { TEXT } from './text';

/** The viewers that come with the app, registered as a plugin's are. */
export const viewers = new ViewerRegistry();
viewers.register(null, [IMAGE, TEXT]);
