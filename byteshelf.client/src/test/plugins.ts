import type { PluginView } from '../services/pluginService';

/** A plugin as the server lists it, with a module unless `parts` say otherwise. */
export function pluginView(id: string, parts: Partial<PluginView> = {}): PluginView {
  return { id, name: id, version: '1', moduleUrl: `/plugins/${id}/hash/plugin.js`, stylesheetUrl: null, readerProblem: null, makerProblem: null, ...parts };
}
