// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useInvalidationStream } from './useInvalidationStream';
import { INVALIDATION_RESOURCES } from '../lib/invalidationMap';
import { queryClient } from '../lib/queryClient';
import { queryKeys } from '../lib/queryKeys';
import { followInvalidations, type InvalidationHandlers } from '../services/invalidationStream';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../services/invalidationStream');

function Page({ isSignedIn }: { isSignedIn: boolean }) {
  useInvalidationStream(isSignedIn);
  return null;
}

describe('following the server\'s changes', () => {
  const stop = vi.fn();
  let root: Root;
  let invalidated: unknown[];

  beforeEach(() => {
    vi.mocked(followInvalidations).mockReset().mockReturnValue(stop);
    stop.mockReset();
    invalidated = [];
    vi.spyOn(queryClient, 'invalidateQueries').mockImplementation(async (filters) => void invalidated.push(filters?.queryKey));
    root = createRoot(document.body.appendChild(document.createElement('div')));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  function follow(): InvalidationHandlers {
    act(() => root.render(<Page isSignedIn />));
    return vi.mocked(followInvalidations).mock.calls[0][0];
  }

  it('makes what a changed resource holds stale, and nothing else', () => {
    follow().onResource('shelf-access');

    expect(invalidated).toEqual([queryKeys.shelfAccess.root]);
  });

  it.each(['unheard-of', 'constructor'])('leaves everything alone for %s, which a newer server may publish', (resource) => {
    follow().onResource(resource);

    expect(invalidated).toEqual([]);
  });

  it('makes everything stale once the stream is back after a drop', () => {
    follow().onMissed();

    expect(invalidated).toHaveLength(INVALIDATION_RESOURCES.length);
    expect(invalidated).toEqual(expect.arrayContaining([queryKeys.users.root, queryKeys.shelves.root, queryKeys.shelfContents.root, queryKeys.shelfAccess.root]));
  });

  it('follows while signed in only', () => {
    act(() => root.render(<Page isSignedIn={false} />));
    expect(followInvalidations).not.toHaveBeenCalled();

    follow();
    expect(stop).not.toHaveBeenCalled();

    act(() => root.render(<Page isSignedIn={false} />));
    expect(stop).toHaveBeenCalledOnce();
  });
});
