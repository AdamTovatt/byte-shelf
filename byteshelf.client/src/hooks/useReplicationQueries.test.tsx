// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { usePromote } from './useReplicationQueries';
import { queryKeys } from '../lib/queryKeys';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../services/replicationService', () => ({ promote: async () => ({}) }));

let root: Root;

beforeEach(() => {
  root = createRoot(document.body.appendChild(document.createElement('div')));
});

afterEach(() => {
  act(() => root.unmount());
  document.body.innerHTML = '';
});

it.each([
  ['every shelf, which says whether it is on a replica', queryKeys.shelves.info('docs')],
  ['the replication status', queryKeys.replication.status],
])('has %s read again as the server is promoted', async (_, key) => {
  const client = new QueryClient();
  client.setQueryData(key, {});
  let promote: () => Promise<unknown>;
  function Probe() {
    promote = usePromote().mutateAsync;
    return null;
  }
  act(() => root.render(<QueryClientProvider client={client}><Probe /></QueryClientProvider>));

  await act(async () => void await promote());

  expect(client.getQueryState(key)?.isInvalidated).toBe(true);
});
