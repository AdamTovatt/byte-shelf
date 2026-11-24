// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { usePreviewParam, type PreviewParam } from './usePreviewParam';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

describe('the preview in the URL', () => {
  let root: Root;
  let preview: PreviewParam;
  let location: string;
  let navigate: ReturnType<typeof useNavigate>;

  function Probe() {
    preview = usePreviewParam();
    const { pathname, search } = useLocation();
    location = pathname + search;
    navigate = useNavigate();
    return null;
  }

  /** Starts a history of `entries`, at the last one. */
  function start(entries: string[]): void {
    root = createRoot(document.createElement('div'));
    act(() => root.render(<MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}><Probe /></MemoryRouter>));
  }

  beforeEach(() => start(['/before', '/page?tab=1']));
  afterEach(() => act(() => root.unmount()));

  it('opens beside what the URL holds, and closes to where it was opened from', () => {
    act(() => preview.open('a b'));
    expect([location, preview.id]).toEqual(['/page?tab=1&preview=a+b', 'a b']);

    act(() => preview.moveTo('c'));
    expect([location, preview.id]).toEqual(['/page?tab=1&preview=c', 'c']);

    act(() => preview.close());
    expect([location, preview.id]).toEqual(['/page?tab=1', null]);
    act(() => navigate(-1));
    expect(location).toBe('/before');
  });

  it('stays on the page when it is closed twice before the first has taken effect', () => {
    act(() => preview.open('a'));

    act(() => {
      preview.close();
      preview.close();
    });

    expect(location).toBe('/page?tab=1');
  });

  it('closes again once opened again, by going forward or anew', () => {
    act(() => preview.open('a'));
    act(() => preview.close());
    act(() => navigate(1));
    expect(preview.id).toBe('a');

    act(() => preview.close());
    act(() => preview.open('b'));
    act(() => preview.close());

    expect([location, preview.id]).toEqual(['/page?tab=1', null]);
  });

  it('moves one that is open on when another is opened', () => {
    act(() => preview.open('a'));
    act(() => preview.open('b'));
    expect(preview.id).toBe('b');

    act(() => preview.close());

    expect([location, preview.id]).toEqual(['/page?tab=1', null]);
  });

  it('closes one after another in the history', () => {
    act(() => preview.open('a'));
    act(() => navigate({ search: 'tab=1&preview=b' }, { state: { previewOpenedHere: true } }));

    act(() => preview.close());
    expect(preview.id).toBe('a');
    act(() => preview.close());

    expect([location, preview.id]).toEqual(['/page?tab=1', null]);
  });

  it('closes one a link opened by staying on the page', () => {
    act(() => root.unmount());
    start(['/before', '/page?preview=a']);

    act(() => preview.close());

    expect([location, preview.id]).toEqual(['/page', null]);
    act(() => navigate(-1));
    expect(location).toBe('/before');
  });
});
