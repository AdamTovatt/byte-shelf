// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginHost, browserTransport, type PluginTransport } from './pluginHost';
import { definePlugin } from '../../plugin/pluginDefinition';
import type { PluginView } from '../../services/pluginService';

function plugin(id: string, parts: Partial<PluginView> = {}): PluginView {
  return { id, name: id, version: '1', moduleUrl: `/plugins/${id}/hash/plugin.js`, stylesheetUrl: null, ...parts };
}

describe('the plugin host', () => {
  let transport: { importModule: ReturnType<typeof vi.fn<PluginTransport['importModule']>>; loadStylesheet: ReturnType<typeof vi.fn<PluginTransport['loadStylesheet']>> };
  let host: PluginHost;

  let removed: string[];
  let logged: string[];

  beforeEach(() => {
    removed = [];
    logged = [];
    vi.spyOn(console, 'error').mockImplementation((message: string) => void logged.push(message));
    transport = {
      importModule: vi.fn<PluginTransport['importModule']>(async (url) => ({ default: definePlugin({ from: url }) })),
      loadStylesheet: vi.fn<PluginTransport['loadStylesheet']>((url) => ({ loaded: Promise.resolve(), remove: () => void removed.push(url) })),
    };
    host = new PluginHost(transport);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads each plugin with its stylesheet, in order of id', async () => {
    await host.load([plugin('b', { stylesheetUrl: '/plugins/b/hash/plugin.css' }), plugin('a')]);

    expect(host.state().plugins).toEqual([
      { id: 'a', definition: { from: '/plugins/a/hash/plugin.js' } },
      { id: 'b', definition: { from: '/plugins/b/hash/plugin.js' } },
    ]);
    expect(transport.loadStylesheet.mock.calls).toEqual([['/plugins/b/hash/plugin.css']]);
    expect(removed).toEqual([]);
    expect(logged).toEqual([]);
  });

  it('leaves out a plugin that has no part in the web UI', async () => {
    await host.load([plugin('server-only', { moduleUrl: null })]);

    expect(host.state().plugins).toEqual([]);
    expect(transport.importModule).not.toHaveBeenCalled();
  });

  it('loads each once, whatever it is asked for again', async () => {
    await Promise.all([host.load([plugin('a')]), host.load([plugin('a')])]);
    await host.load([plugin('a', { moduleUrl: '/plugins/a/other/plugin.js' }), plugin('b')]);

    expect(transport.importModule.mock.calls).toEqual([['/plugins/a/hash/plugin.js'], ['/plugins/b/hash/plugin.js']]);
  });

  it('shows a plugin once its stylesheet is there, not before', async () => {
    let styled = (): void => undefined;
    transport.loadStylesheet.mockReturnValue({ loaded: new Promise((resolve) => { styled = resolve; }), remove: () => undefined });

    const loading = host.load([plugin('a', { stylesheetUrl: '/a.css' })]);
    await Promise.resolve();
    await Promise.resolve();
    expect(host.state().plugins).toEqual([]);

    styled();
    await loading;
    expect(host.state().plugins).toHaveLength(1);
  });

  it('skips a plugin that fails, alone, and says why', async () => {
    transport.importModule.mockImplementation(async (url) => {
      if (url.includes('/throws/')) throw new Error('It threw.');
      if (url.includes('/throws-text/')) throw 'It threw text.';
      return { default: definePlugin({}) };
    });
    transport.loadStylesheet.mockReturnValue({ loaded: Promise.reject(new Error('Its stylesheet could not be loaded.')), remove: () => undefined });

    await host.load([plugin('throws'), plugin('throws-text'), plugin('unstyled', { stylesheetUrl: '/unstyled.css' }), plugin('good')]);

    expect(host.state().plugins.map(({ id }) => id)).toEqual(['good']);
    expect([...host.state().failures].sort()).toEqual([
      ['throws', 'It threw.'],
      ['throws-text', 'It threw text.'],
      ['unstyled', 'Its stylesheet could not be loaded.'],
    ]);
    expect(logged.sort()).toEqual(['throws', 'throws-text', 'unstyled'].map((id) => `The plugin ${id} could not be loaded.`));
  });

  it.each([
    ['nothing', {}],
    ['null', { default: null }],
    ['a string', { default: 'plugin' }],
    ['a list', { default: [] }],
    ['an object not made with definePlugin', { default: {} }],
  ])('skips a plugin whose module exports %s', async (_, module) => {
    transport.importModule.mockResolvedValue(module);

    await host.load([plugin('a')]);

    expect(host.state().plugins).toEqual([]);
    expect([...host.state().failures]).toEqual([['a', 'Its module has no default export made with definePlugin.']]);
  });

  it('takes the stylesheet of a plugin that fails out of the page, though it has not loaded yet', async () => {
    transport.importModule.mockRejectedValue(new Error('It threw.'));
    transport.loadStylesheet.mockImplementation((url) => ({ loaded: new Promise(() => undefined), remove: () => void removed.push(url) }));

    await host.load([plugin('a', { stylesheetUrl: '/a.css' })]);

    expect(removed).toEqual(['/a.css']);
  });

  it('tells whoever follows it of each change, with a state of its own', async () => {
    const seen: string[][] = [];
    const stop = host.subscribe(() => seen.push(host.state().plugins.map(({ id }) => id)));
    const before = host.state();

    await host.load([plugin('a')]);
    stop();
    await host.load([plugin('b')]);

    expect(seen).toEqual([['a']]);
    expect(before.plugins).toEqual([]);
    expect(host.state()).toBe(host.state());
  });
});

describe('loading a stylesheet into the page', () => {
  function load(): { link: HTMLLinkElement; stylesheet: ReturnType<PluginTransport['loadStylesheet']> } {
    // The link as the page has it, which happy-dom would go and load when it is put there.
    const append = vi.spyOn(document.head, 'append').mockImplementation(() => undefined);
    const stylesheet = browserTransport.loadStylesheet('/plugins/a/hash/plugin.css');
    return { link: append.mock.calls[0][0] as HTMLLinkElement, stylesheet };
  }

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('is done once the page has it', async () => {
    const { link, stylesheet } = load();
    expect([link.rel, link.getAttribute('href')]).toEqual(['stylesheet', '/plugins/a/hash/plugin.css']);

    let isLoaded = false;
    void stylesheet.loaded.then(() => { isLoaded = true; });
    await Promise.resolve();
    expect(isLoaded).toBe(false);

    link.dispatchEvent(new Event('load'));
    await stylesheet.loaded;
  });

  it('fails when the page cannot get it', async () => {
    const { link, stylesheet } = load();

    link.dispatchEvent(new Event('error'));

    await expect(stylesheet.loaded).rejects.toThrow('Its stylesheet could not be loaded.');
  });

  it('can be undone', () => {
    const { link, stylesheet } = load();
    const remove = vi.spyOn(link, 'remove');

    stylesheet.remove();

    expect(remove).toHaveBeenCalledOnce();
  });
});
