// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { it, vi } from 'vitest';
import App from './App';
import { ThemeProvider } from './ui';
import { leaveStartupToast } from './lib/startupToast';
import { shows } from './test/dom';
import { json, stubServer } from './test/fakeServer';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

it('shows the toast left for it as it starts', async () => {
  stubServer(async () => json({}));
  leaveStartupToast('Paired.');
  const root = createRoot(document.body.appendChild(document.createElement('div')));

  await act(async () => root.render(<ThemeProvider><QueryClientProvider client={new QueryClient()}><App /></QueryClientProvider></ThemeProvider>));

  await shows('Paired.');
  act(() => root.unmount());
  vi.unstubAllGlobals();
});
