// @vitest-environment happy-dom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useViewers } from './useViewers';
import { BUILT_IN_VIEWERS } from '../components/preview/viewers';
import { PluginHost } from '../lib/plugins/pluginHost';
import type { Viewer, ViewerRegistry } from '../lib/preview/viewerRegistry';
import { definePlugin } from '../lib/plugins/pluginDefinition';
import type { ShelfFile } from '../services/shelfService';
import { pluginView } from '../test/plugins';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const page = vi.hoisted(() => ({ host: undefined as unknown }));
vi.mock('../lib/plugins/pluginHost', async (original) => ({
  ...await original<typeof import('../lib/plugins/pluginHost')>(),
  get pluginHost() {
    return page.host;
  },
}));

function viewer(id: string, mediaTypes: string[]): Viewer {
  return { id, mediaTypes, extensions: [], component: () => null };
}

const definitions: Record<string, ReturnType<typeof definePlugin>> = {
  '/b.js': definePlugin({ viewers: [viewer('from-b', ['text/markdown'])] }),
  '/a.js': definePlugin({ viewers: [viewer('from-a', ['text/markdown', 'application/json'])] }),
  '/none.js': definePlugin({}),
};

describe('the viewers', () => {
  let root: Root;
  let host: PluginHost;
  let viewers: ViewerRegistry;
  let drawn: number;

  function Page() {
    viewers = useViewers();
    drawn++;
    return null;
  }

  function load(id: string): Promise<void> {
    return act(() => host.load([pluginView(id, { moduleUrl: `/${id}.js` })]));
  }

  function picked(contentType: string): string | undefined {
    return viewers.viewerFor({ name: 'x', contentType } as ShelfFile)?.viewer?.id;
  }

  beforeEach(() => {
    drawn = 0;
    page.host = host = new PluginHost({ importModule: async (url) => ({ default: definitions[url] }), loadStylesheet: () => { throw new Error('It has none.'); } });
    root = createRoot(document.body.appendChild(document.createElement('div')));
    act(() => root.render(<Page />));
  });

  afterEach(() => {
    act(() => root.unmount());
    document.body.innerHTML = '';
  });

  it('are the built-in ones without any plugin', () => {
    expect(viewers).toBe(BUILT_IN_VIEWERS);
  });

  it('are a plugin\'s too once it is loaded, picked before the built-in ones', async () => {
    await load('b');

    expect([picked('text/markdown'), picked('text/plain')]).toEqual(['from-b', 'text']);
  });

  it('are those of every plugin, whichever came first, and of one that has none', async () => {
    await load('b');
    await load('none');
    await load('a');

    // JSON the built-in one takes just as exactly.
    expect([picked('text/markdown'), picked('application/json')]).toEqual(['from-a', 'from-a']);
  });

  it('stay the same ones while no plugin is loaded', async () => {
    await load('b');
    const [before, drawnBefore] = [viewers, drawn];

    act(() => root.render(<Page />));

    expect(drawn).toBeGreaterThan(drawnBefore);
    expect(viewers).toBe(before);
  });
});
