// @vitest-environment happy-dom
import { act, useEffect, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Link } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import App from './App';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function Children({ children }: { children: ReactNode }) {
  return children;
}

vi.mock('./contexts/AuthContext', () => ({ AuthProvider: Children, useAuth: () => ({ isAuthenticated: true }) }));
vi.mock('./components/layout/AppShell', () => ({ AppShell: Children }));
vi.mock('./components/NewVersionBanner', () => ({ NewVersionBanner: () => null }));
vi.mock('./components/StartupToast', () => ({ StartupToast: () => null }));
vi.mock('./hooks/useInvalidationStream', () => ({ useInvalidationStream: () => undefined }));
vi.mock('./hooks/usePluginHost', () => ({ useLoadPlugins: () => undefined }));
vi.mock('./pages/ShelvesPage', () => ({ ShelvesPage: () => <p>the shelves</p> }));
vi.mock('./pages/ShelfPage', () => ({
  ShelfPage: () => {
    useEffect(() => () => { throw new Error('It failed as it went.'); }, []);
    return <Link to="/">Shelves</Link>;
  },
}));

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  vi.spyOn(console, 'error').mockImplementation(() => undefined);
  history.replaceState(null, '', '/shelves/docs');
  container = document.body.appendChild(document.createElement('div'));
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  document.body.innerHTML = '';
  vi.restoreAllMocks();
});

it('goes on to the next page as what a plugin drew in the last one throws as it goes', async () => {
  await act(async () => root.render(<App />));
  await act(async () => container.querySelector('a')!.click());

  expect(container.textContent).toContain('the shelves');
});
