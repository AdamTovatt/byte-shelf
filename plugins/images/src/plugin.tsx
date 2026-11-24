import { definePlugin } from '@byteshelf/plugin';
import { SharpeningViewer } from './SharpeningViewer';
import './plugin.css';

export default definePlugin({
  // Nothing is made of a file stored without a type, whatever its name.
  viewers: [{ id: 'images', mediaTypes: ['image/*'], extensions: [], component: SharpeningViewer }],
});
