import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { AudioViewer } from './AudioViewer';

/** Every recording; the browser plays an untyped one by its bytes. */
export const AUDIO: Viewer = {
  id: 'audio',
  mediaTypes: ['audio/*'],
  extensions: ['.mp3', '.m4a', '.aac', '.wav', '.ogg', '.oga', '.opus', '.flac', '.weba'],
  component: AudioViewer,
};
