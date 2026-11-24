import type { PluginView } from '../../services/pluginService';
import { partsOf, type PluginParts } from './pluginDefinition';
import { getErrorMessage } from '../../ui';

/** A loaded plugin and what it adds. */
export interface RunningPlugin extends PluginParts {
  id: string;
}

export interface PluginHostState {
  /** The plugins loaded so far, by id. */
  plugins: readonly RunningPlugin[];
  /** Why a plugin's part in the web UI could not be loaded, by plugin id. */
  failures: ReadonlyMap<string, string>;
}

/** A stylesheet on its way into the page, or there. */
export interface PluginStylesheet {
  loaded: Promise<void>;
  /** Takes it out of the page, loaded or not. */
  remove: () => void;
}

/** How the host reaches what the server serves: the browser's own, but for tests. */
export interface PluginTransport {
  importModule: (url: string) => Promise<{ default?: unknown }>;
  loadStylesheet: (url: string) => PluginStylesheet;
}

/**
 * Loads the plugins' parts in the web UI and holds what they add, for components to follow through
 * `subscribe`, since plugins arrive after the app is shown. One that fails is skipped, alone. Each is
 * loaded once in the life of the page: a new version of one takes the reload the app offers.
 */
export class PluginHost {
  private readonly transport: PluginTransport;
  private readonly started = new Set<string>();
  private readonly listeners = new Set<() => void>();
  private current: PluginHostState = { plugins: [], failures: new Map() };

  constructor(transport: PluginTransport) {
    this.transport = transport;
  }

  readonly state = (): PluginHostState => this.current;

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Loads those of `plugins` that have a part in the web UI and were not loaded before; resolves when each is loaded or skipped. */
  async load(plugins: readonly PluginView[]): Promise<void> {
    await Promise.all(plugins.map(async ({ id, moduleUrl, stylesheetUrl }) => {
      if (!moduleUrl || this.started.has(id)) return;
      this.started.add(id);

      const stylesheet = stylesheetUrl ? this.transport.loadStylesheet(stylesheetUrl) : undefined;
      try {
        // The styles first seen are the plugin's own, so it is not there before its stylesheet is.
        const [module] = await Promise.all([this.transport.importModule(moduleUrl), stylesheet?.loaded]);
        this.add({ id, ...partsOf(module.default) });
      } catch (caught) {
        stylesheet?.remove();
        console.error(`The plugin ${id} could not be loaded.`, caught);
        this.change({ ...this.current, failures: new Map(this.current.failures).set(id, getErrorMessage(caught, String(caught))) });
      }
    }));
  }

  private add(plugin: RunningPlugin): void {
    this.change({ ...this.current, plugins: [...this.current.plugins, plugin].sort((one, other) => (one.id < other.id ? -1 : 1)) });
  }

  private change(next: PluginHostState): void {
    this.current = next;
    for (const listener of this.listeners) listener();
  }
}

/** Loads from the server by the page itself. */
export const browserTransport: PluginTransport = {
  // As a whole URL, which Vite's dev server leaves to the browser rather than transforming.
  importModule: (url) => import(/* @vite-ignore */ new URL(url, window.location.href).href) as Promise<{ default?: unknown }>,
  loadStylesheet: (url) => {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = url;
    const loaded = new Promise<void>((resolve, reject) => {
      link.onload = () => resolve();
      link.onerror = () => reject(new Error('Its stylesheet could not be loaded.'));
    });
    document.head.append(link);
    return { loaded, remove: () => link.remove() };
  },
};

export const pluginHost = new PluginHost(browserTransport);
