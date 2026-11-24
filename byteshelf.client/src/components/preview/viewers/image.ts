import type { Viewer } from '../../../lib/preview/viewerRegistry';
import { ImageViewer } from './ImageViewer';

/**
 * Every image. The browser shows an untyped one by its bytes, but an SVG only by its type, so `.svg` is
 * not among the extensions.
 */
export const IMAGE: Viewer = {
  id: 'image',
  mediaTypes: ['image/*'],
  extensions: ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.avif', '.bmp', '.ico', '.heic', '.heif'],
  component: ImageViewer,
};
