import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { VideoViewer } from './VideoViewer';

/** Every video; the browser plays an untyped one by its bytes. */
export const VIDEO: Viewer = {
  id: 'video',
  mediaTypes: ['video/*'],
  extensions: ['.mp4', '.m4v', '.mov', '.webm', '.mkv', '.ogv'],
  component: VideoViewer,
};
