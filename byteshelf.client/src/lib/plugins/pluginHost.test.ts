// @vitest-environment happy-dom
import { lazy } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PluginHost, browserTransport, type PluginTransport } from './pluginHost';
import { definePlugin } from './pluginDefinition';
import { pluginView } from '../../test/plugins';

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
      importModule: vi.fn<PluginTransport['importModule']>(async (url) => ({ default: definePlugin({ fileActions: [{ id: url, label: url, showsFor: () => true, run: () => undefined }] }) })),
      loadStylesheet: vi.fn<PluginTransport['loadStylesheet']>((url) => ({ loaded: Promise.resolve(), remove: () => void removed.push(url) })),
    };
    host = new PluginHost(transport);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads each plugin with its stylesheet, in order of id', async () => {
    await host.load([pluginView('b', { stylesheetUrl: '/plugins/b/hash/plugin.css' }), pluginView('a')]);

    expect(host.state().plugins.map(({ id, viewers, fileActions }) => [id, viewers, fileActions.map((action) => action.id)])).toEqual([
      ['a', [], ['/plugins/a/hash/plugin.js']],
      ['b', [], ['/plugins/b/hash/plugin.js']],
    ]);
    expect(transport.loadStylesheet.mock.calls).toEqual([['/plugins/b/hash/plugin.css']]);
    expect(removed).toEqual([]);
    expect(logged).toEqual([]);
  });

  it('leaves out a plugin that has no part in the web UI', async () => {
    await host.load([pluginView('server-only', { moduleUrl: null })]);

    expect(host.state().plugins).toEqual([]);
    expect(transport.importModule).not.toHaveBeenCalled();
  });

  it('loads each once, whatever it is asked for again', async () => {
    await Promise.all([host.load([pluginView('a')]), host.load([pluginView('a')])]);
    await host.load([pluginView('a', { moduleUrl: '/plugins/a/other/plugin.js' }), pluginView('b')]);

    expect(transport.importModule.mock.calls).toEqual([['/plugins/a/hash/plugin.js'], ['/plugins/b/hash/plugin.js']]);
  });

  it('shows a plugin once its stylesheet is there, not before', async () => {
    let styled = (): void => undefined;
    transport.loadStylesheet.mockReturnValue({ loaded: new Promise((resolve) => { styled = resolve; }), remove: () => undefined });

    const loading = host.load([pluginView('a', { stylesheetUrl: '/a.css' })]);
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

    await host.load([pluginView('throws'), pluginView('throws-text'), pluginView('unstyled', { stylesheetUrl: '/unstyled.css' }), pluginView('good')]);

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

    await host.load([pluginView('a')]);

    expect(host.state().plugins).toEqual([]);
    expect([...host.state().failures]).toEqual([['a', 'Its module has no default export made with definePlugin.']]);
  });

  const viewer = { id: 'a', mediaTypes: ['text/plain'], extensions: ['.txt'], component: () => null };
  const action = { id: 'a', label: 'Act', showsFor: () => true, run: () => undefined };
  const folderType = { suffix: 'album', label: 'Album', icon: () => null, component: () => null };
  const lacking = (name: string) => `One of its ${name} lacks what each must have.`;

  it.each<[string, object, string]>([
    ['viewers that are no list', { viewers: {} }, 'Its viewers are not a list.'],
    ['a missing viewer', { viewers: [viewer, undefined] }, lacking('viewers')],
    ['a viewer without an id', { viewers: [{ ...viewer, id: undefined }] }, lacking('viewers')],
    ['a viewer without types', { viewers: [{ ...viewer, mediaTypes: undefined }] }, lacking('viewers')],
    ['a viewer of what is no type', { viewers: [{ ...viewer, mediaTypes: [1] }] }, lacking('viewers')],
    ['a viewer without extensions', { viewers: [{ ...viewer, extensions: undefined }] }, lacking('viewers')],
    ['a viewer without a component', { viewers: [{ ...viewer, component: undefined }] }, lacking('viewers')],
    ['a viewer whose unavailable is no function', { viewers: [{ ...viewer, unavailable: 'no' }] }, lacking('viewers')],
    ['two viewers of one id', { viewers: [viewer, { ...viewer }] }, 'Two of its viewers have the id a.'],
    ['file actions that are no list', { fileActions: 1 }, 'Its fileActions are not a list.'],
    ['a missing file action', { fileActions: [null] }, lacking('fileActions')],
    ['a file action without a label', { fileActions: [{ ...action, label: undefined }] }, lacking('fileActions')],
    ['a file action that cannot be asked whether it shows', { fileActions: [{ ...action, showsFor: undefined }] }, lacking('fileActions')],
    ['a file action that cannot be run', { fileActions: [{ ...action, run: undefined }] }, lacking('fileActions')],
    ['two file actions of one id', { fileActions: [action, { ...action }] }, 'Two of its fileActions have the id a.'],
    ['folder types that are no list', { folderTypes: {} }, 'Its folderTypes are not a list.'],
    ['a missing folder type', { folderTypes: [null] }, lacking('folderTypes')],
    ['a folder type without a suffix', { folderTypes: [{ ...folderType, suffix: undefined }] }, lacking('folderTypes')],
    ['a folder type with a short suffix', { folderTypes: [{ ...folderType, suffix: 'a' }] }, lacking('folderTypes')],
    ['a folder type whose suffix is not all letters and digits', { folderTypes: [{ ...folderType, suffix: '.album' }] }, lacking('folderTypes')],
    ['a folder type whose suffix is not lowercase', { folderTypes: [{ ...folderType, suffix: 'Album' }] }, lacking('folderTypes')],
    ['a folder type without a label', { folderTypes: [{ ...folderType, label: undefined }] }, lacking('folderTypes')],
    ['a folder type without an icon', { folderTypes: [{ ...folderType, icon: undefined }] }, lacking('folderTypes')],
    ['a folder type without a component', { folderTypes: [{ ...folderType, component: undefined }] }, lacking('folderTypes')],
    ['two folder types of one suffix', { folderTypes: [folderType, { ...folderType }] }, 'Two of its folderTypes have the suffix album.'],
  ])('skips a plugin with %s, which the app would fail on as it draws', async (_, definition, reason) => {
    transport.importModule.mockResolvedValue({ default: definePlugin(definition) });

    await host.load([pluginView('a')]);

    expect(host.state().plugins).toEqual([]);
    expect([...host.state().failures]).toEqual([['a', reason]]);
  });

  it('takes a folder type', async () => {
    transport.importModule.mockResolvedValue({ default: definePlugin({ folderTypes: [folderType] }) });

    await host.load([pluginView('a')]);

    expect(host.state().plugins.map(({ folderTypes }) => folderTypes)).toEqual([[folderType]]);
  });

  it('takes a viewer drawn by a component that is loaded when it is shown', async () => {
    const component = lazy(async () => ({ default: () => null }));
    transport.importModule.mockResolvedValue({ default: definePlugin({ viewers: [{ ...viewer, component }] }) });

    await host.load([pluginView('a')]);

    expect(host.state().plugins.map(({ viewers }) => viewers[0].component)).toEqual([component]);
  });

  it('takes the stylesheet of a plugin that fails out of the page, though it has not loaded yet', async () => {
    transport.importModule.mockRejectedValue(new Error('It threw.'));
    transport.loadStylesheet.mockImplementation((url) => ({ loaded: new Promise(() => undefined), remove: () => void removed.push(url) }));

    await host.load([pluginView('a', { stylesheetUrl: '/a.css' })]);

    expect(removed).toEqual(['/a.css']);
  });

  it('tells whoever follows it of each change, with a state of its own', async () => {
    const seen: string[][] = [];
    const stop = host.subscribe(() => seen.push(host.state().plugins.map(({ id }) => id)));
    const before = host.state();

    await host.load([pluginView('a')]);
    stop();
    await host.load([pluginView('b')]);

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
