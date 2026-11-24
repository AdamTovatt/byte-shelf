// @vitest-environment happy-dom
import { StrictMode, act } from 'react';
import { createRoot } from 'react-dom/client';
import toast from 'react-hot-toast';
import { expect, it, vi } from 'vitest';
import { StartupToast } from './StartupToast';
import { leaveStartupToast } from '../lib/startupToast';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

it('shows what the page before left to say, once', () => {
  const shown = vi.spyOn(toast, 'success').mockReturnValue('');
  leaveStartupToast('Paired.');

  for (let started = 0; started < 2; started++) {
    const root = createRoot(document.createElement('div'));
    act(() => root.render(<StrictMode><StartupToast /></StrictMode>));
    act(() => root.unmount());
  }

  expect(shown.mock.calls).toEqual([['Paired.']]);
});
