import { definePlugin } from '@byteshelf/plugin';
import { IMAGE_TYPES } from './imageTypes';
import { SharpeningViewer } from './SharpeningViewer';
import './plugin.css';

export default definePlugin({
  // Nothing is made of a file stored without a type, whatever its name.
  viewers: [{ id: 'images', mediaTypes: IMAGE_TYPES, extensions: [], component: SharpeningViewer }],
});
