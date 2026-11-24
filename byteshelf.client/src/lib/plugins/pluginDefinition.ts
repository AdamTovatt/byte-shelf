import type { Viewer } from '../preview/viewerRegistry';
import type { FileAction } from './fileAction';

/** What a plugin adds to the web UI, as `definePlugin` takes it. */
export interface PluginDefinition {
  /** What the preview shows files with; one that loads code when it is first shown is a `lazy` component. */
  readonly viewers?: readonly Viewer[];
  readonly fileActions?: readonly FileAction[];
}

/** What a plugin adds to the web UI, each list there. */
export type PluginParts = Required<PluginDefinition>;

const defined = new WeakSet<object>();

/** The default export of a plugin's module. */
export function definePlugin(definition: PluginDefinition): PluginDefinition {
  defined.add(definition);
  return definition;
}

/**
 * What the plugin whose module exports `exported` adds. Fails on what the app would otherwise fail on as it
 * draws, where that would take the page down: a plugin's module may be built without its types checked.
 */
export function partsOf(exported: unknown): PluginParts {
  if (typeof exported !== 'object' || exported === null || !defined.has(exported)) throw new Error('Its module has no default export made with definePlugin.');

  const { viewers = [], fileActions = [] } = exported as Record<keyof PluginDefinition, unknown>;
  return {
    viewers: listOf<Viewer>('viewers', viewers, ({ mediaTypes, extensions, component, unavailable }) => (
      isTexts(mediaTypes) && isTexts(extensions) && isComponent(component) && (unavailable === undefined || typeof unavailable === 'function')
    )),
    fileActions: listOf<FileAction>('fileActions', fileActions, ({ label, showsFor, run }) => (
      typeof label === 'string' && typeof showsFor === 'function' && typeof run === 'function'
    )),
  };
}

type Unchecked<T> = { [Key in keyof T]?: unknown };

/** `list`, named `name`, of what has an id apart from the others' and which `isOne` takes. */
function listOf<T extends { id: string }>(name: string, list: unknown, isOne: (each: Unchecked<T>) => boolean): readonly T[] {
  if (!Array.isArray(list)) throw new Error(`Its ${name} are not a list.`);

  const ids = new Set<unknown>();
  for (const each of list as (Unchecked<T> | null | undefined)[]) {
    if (!each || typeof each.id !== 'string' || !isOne(each)) throw new Error(`One of its ${name} lacks what each must have.`);
    if (ids.has(each.id)) throw new Error(`Two of its ${name} have the id ${each.id}.`);
    ids.add(each.id);
  }
  return list as T[];
}

function isTexts(value: unknown): boolean {
  return Array.isArray(value) && value.every((each) => typeof each === 'string');
}

/** A function, or an object as `lazy` and `memo` make. */
function isComponent(value: unknown): boolean {
  return typeof value === 'function' || (typeof value === 'object' && value !== null);
}
