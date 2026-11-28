import { definePlugin } from '@byteshelf/plugin';
import { VideoPlayer } from './VideoPlayer';
import './plugin.css';

export default definePlugin({
  // Ahead of the app's own player, which takes what this one leaves to it. Nothing is made of a file stored without a type.
  viewers: [{ id: 'video', mediaTypes: ['video/*'], extensions: [], component: VideoPlayer }],
});
