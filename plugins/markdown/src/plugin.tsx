import { lazy } from 'react';
import { definePlugin } from '@byteshelf/plugin';
import './plugin.css';

export default definePlugin({
  viewers: [{ id: 'markdown', mediaTypes: ['text/markdown'], extensions: ['.md'], component: lazy(() => import('./MarkdownViewer')) }],
});
