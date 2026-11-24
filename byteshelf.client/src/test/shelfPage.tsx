import { StrictMode, act } from 'react';
import { createRoot } from 'react-dom/client';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DialogHost } from '../contexts/DialogHost';
import { MobileTopBarContext } from '../contexts/MobileTopBarContext';
import { SHELF_ROUTE_PATTERNS } from '../lib/shelfPaths';
import { ShelfPage } from '../pages/ShelfPage';
import { saveTokens } from '../services/authService';
import { inAnHour } from './fakeServer';
import { installFakeWebLocks } from './fakeWebLocks';
import { pageHistory } from './pageHistory';

/** Signs someone in, to a browser that keeps nothing of anyone before them. */
export function signIn(): void {
  localStorage.clear();
  sessionStorage.clear();
  installFakeWebLocks();
  saveTokens({ token: 't', expiresAt: inAnHour(), refreshToken: 'r' });
}

/** Opens the shelf page at `url` in a tab of its own: no history and nothing cached. */
export async function renderShelfPage(url: string, { strict = false, phone = false } = {}) {
  const container = document.body.appendChild(document.createElement('div'));
  const root = createRoot(container);
  const page = pageHistory();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, retryDelay: 0 } } });
  const bar = document.body.appendChild(document.createElement('div'));
  const drawn = (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <MobileTopBarContext.Provider value={{ actions: phone ? bar : null, bar: null }}>
          <DialogHost>
            <Routes>
              <Route path={SHELF_ROUTE_PATTERNS.shelf} element={<ShelfPage />} />
              <Route path={SHELF_ROUTE_PATTERNS.files} element={<ShelfPage />} />
            </Routes>
          </DialogHost>
        </MobileTopBarContext.Provider>
        <page.Follow />
      </MemoryRouter>
    </QueryClientProvider>
  );
  await act(async () => root.render(strict ? <StrictMode>{drawn}</StrictMode> : drawn));

  /** Closes the tab. */
  function leave(): void {
    act(() => root.unmount());
    document.body.innerHTML = '';
  }

  return { container, client, page, leave };
}

/** The row of a listing that opens `name`, or null when none shows. */
export function row(name: string): HTMLButtonElement | null {
  return document.body.querySelector(`[data-selectable-row] button[aria-label="${name}"]`);
}
